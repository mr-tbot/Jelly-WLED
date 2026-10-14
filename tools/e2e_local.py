#!/usr/bin/env python3
"""End-to-end check of the Jelly-WLED plugin against a local Jellyfin without any real player.

What it does, in order:
  1. Completes the startup wizard if needed (admin user "jellywled" / "jellywled").
  2. Adds a "Movies" library pointing at the fixtures folder and waits for the three fixtures to appear.
  3. Writes the plugin configuration: one device aimed at the DDP simulator (host/ports from the
     arguments), following every session, 25 fps, no smoothing so colors can be asserted exactly.
  4. Starts a fake playback session for "Edges 60s" with the Sessions API (PlaybackStart, then
     progress reports every 3 s like the Android TV app), so the plugin follows it like a real client.
  5. Reads the simulator's CSV and checks that the per-side colors follow the fixture schedule
     (top: red, green, blue, white every 10 s; right starts at green; bottom at blue; left at white),
     allowing for analysis start-up and smoothing.
Run tools/ddp_sim.py first, for example:
  python3 -I tools/ddp_sim.py --port 4049 --http-port 8089 --top 32 --right 18 --bottom 32 --left 18 --csv /tmp/jw.csv
Then:
  python3 -I tools/e2e_local.py --server http://127.0.0.1:8096 --fixtures "$PWD/tools/fixtures/movies" \
      --sim-host 127.0.0.1 --sim-ddp 4049 --sim-http 8089 --csv /tmp/jw.csv
Exit code 0 when every check passes.
"""
import argparse
import csv
import json
import sys
import time
import urllib.error
import urllib.request
import uuid

PLUGIN_ID = "7568fce5-c039-4d52-952f-38487b033df0"
AUTH_HEADER = 'MediaBrowser Client="jelly-wled-e2e", Device="e2e", DeviceId="%s", Version="1.0.0"'
SCHEDULE = ["red", "green", "blue", "white"]
COLORS = {"red": (255, 0, 0), "green": (0, 255, 0), "blue": (0, 0, 255), "white": (255, 255, 255)}
PHASE = {"top": 0, "right": 1, "bottom": 2, "left": 3}


class Api:
    def __init__(self, server, token=None, device_id=None):
        self.server = server.rstrip("/")
        self.token = token
        self.device_id = device_id or str(uuid.uuid4())

    def call(self, method, path, body=None, raw=False, timeout=60):
        data = None
        headers = {"Accept": "application/json"}
        auth = AUTH_HEADER % self.device_id
        if self.token:
            auth += ', Token="%s"' % self.token
        headers["Authorization"] = auth
        if body is not None:
            data = json.dumps(body).encode()
            headers["Content-Type"] = "application/json"
        req = urllib.request.Request(self.server + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                payload = r.read()
                if raw:
                    return r.status, payload
                return r.status, (json.loads(payload) if payload.strip() else None)
        except urllib.error.HTTPError as e:
            payload = e.read()
            try:
                return e.code, json.loads(payload)
            except Exception:
                return e.code, payload[:300].decode("utf-8", "replace")


def log(msg):
    print(time.strftime("%H:%M:%S"), msg, flush=True)


def expected_color(side, t):
    return COLORS[SCHEDULE[(int(t // 10) + PHASE[side]) % 4]]


def close(a, b, tol):
    return all(abs(int(x) - int(y)) <= tol for x, y in zip(a, b))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--server", default="http://127.0.0.1:8096")
    ap.add_argument("--fixtures", required=True, help="absolute path of tools/fixtures/movies as the server sees it")
    ap.add_argument("--sim-host", default="127.0.0.1")
    ap.add_argument("--sim-ddp", type=int, default=4049)
    ap.add_argument("--sim-http", type=int, default=8089)
    ap.add_argument("--csv", required=True, help="the simulator's --csv file")
    ap.add_argument("--play-seconds", type=int, default=45)
    ap.add_argument("--start-ms", type=int, default=0, help="playback start position in ms")
    ap.add_argument("--seek-after", type=float, default=0, help="seconds into playback at which the fake client seeks (0 = no seek)")
    ap.add_argument("--seek-to-ms", type=int, default=0, help="position the fake client seeks to")
    ap.add_argument("--delete-track", action="store_true", help="delete the item's track first (tests on-demand analysis)")
    ap.add_argument("--extra-csv", action="append", default=[], help="another simulator CSV to check against the same timeline (for example the one a bridge app feeds)")
    ap.add_argument("--user", default="jellywled")
    ap.add_argument("--password", default="jellywled")
    args = ap.parse_args()

    api = Api(args.server)
    status, info = api.call("GET", "/System/Info/Public")
    if status != 200:
        log("server not reachable: %s %s" % (status, info))
        return 2
    log("server %s %s" % (info.get("ServerName"), info.get("Version")))

    if not info.get("StartupWizardCompleted"):
        log("completing the startup wizard")
        api.call("POST", "/Startup/Configuration", {"UICulture": "en-US", "MetadataCountryCode": "US", "PreferredMetadataLanguage": "en"})
        api.call("GET", "/Startup/User")
        api.call("POST", "/Startup/User", {"Name": args.user, "Password": args.password})
        api.call("POST", "/Startup/RemoteAccess", {"EnableRemoteAccess": True, "EnableAutomaticPortMapping": False})
        api.call("POST", "/Startup/Complete")

    status, auth = api.call("POST", "/Users/AuthenticateByName", {"Username": args.user, "Pw": args.password})
    if status != 200:
        log("login failed: %s %s" % (status, auth))
        return 2
    api.token = auth["AccessToken"]
    user_id = auth["User"]["Id"]
    log("logged in as %s" % args.user)

    status, folders = api.call("GET", "/Library/VirtualFolders")
    if not any(f.get("Name") == "Movies" for f in folders or []):
        log("adding Movies library at %s" % args.fixtures)
        status, _ = api.call("POST", "/Library/VirtualFolders?name=Movies&collectionType=movies&refreshLibrary=true",
                             {"LibraryOptions": {"PathInfos": [{"Path": args.fixtures}], "EnableRealtimeMonitor": False,
                                                 "EnableInternetProviders": False, "SaveLocalMetadata": False}})
        log("library add -> %s" % status)
        time.sleep(2)
        api.call("POST", "/Library/Refresh")  # the refresh that comes with the add can race the new path
    else:
        api.call("POST", "/Library/Refresh")

    item = None
    for _ in range(90):
        status, res = api.call("GET", "/Items?IncludeItemTypes=Movie&Recursive=true&SearchTerm=Edges&userId=%s" % user_id)
        items = (res or {}).get("Items", []) if isinstance(res, dict) else []
        if items:
            item = items[0]
            break
        time.sleep(2)
    if item is None:
        log("fixture 'Edges 60s' did not appear in the library")
        return 2
    log("item %s (%s) runtime %s ticks" % (item["Name"], item["Id"], item.get("RunTimeTicks")))

    if args.delete_track:
        status, _ = api.call("DELETE", "/JellyWled/Track/%s" % item["Id"])
        log("deleted track -> %s" % status)

    status, cfg = api.call("GET", "/Plugins/%s/Configuration" % PLUGIN_ID)
    if status != 200:
        log("plugin configuration not readable: %s %s (is the plugin loaded?)" % (status, cfg))
        return 2
    device_id = str(uuid.uuid4())
    cfg["AnalyzeOnPlayback"] = True
    cfg["EnableServerOutput"] = True
    cfg["DetectLetterbox"] = True
    cfg["Devices"] = [{
        "Id": device_id, "Name": "ddp-sim", "Enabled": True, "Host": args.sim_host, "DdpPort": args.sim_ddp, "HttpPort": args.sim_http,
        "LedsTop": 32, "LedsRight": 18, "LedsBottom": 32, "LedsLeft": 18, "StartCorner": "top_left", "Direction": "cw", "Mode": "ring",
        "Brightness": 100, "Saturation": 100, "Gamma": 1.0, "Smoothing": 0, "Fps": 25, "SyncOffsetMs": 0, "RestoreState": True,
        "FollowAllSessions": True, "FollowDeviceIds": [], "FollowUserIds": [],
    }]
    status, _ = api.call("POST", "/Plugins/%s/Configuration" % PLUGIN_ID, cfg)
    log("plugin configuration saved -> %s" % status)
    time.sleep(3)

    status, st = api.call("GET", "/JellyWled/Status")
    log("status -> %s devices=%s" % (status, [(d["name"], d["state"]) for d in (st or {}).get("devices", [])]))
    if status != 200:
        return 2

    # Fake client: PlaybackStart then progress every 3 s.
    play_session = str(uuid.uuid4())
    t0 = time.monotonic()
    base = {"ItemId": item["Id"], "PlaySessionId": play_session, "MediaSourceId": item["Id"], "CanSeek": True,
            "PlayMethod": "DirectPlay", "IsPaused": False, "IsMuted": False, "RepeatMode": "RepeatNone", "PlaybackOrder": "Default"}
    start = dict(base, PositionTicks=args.start_ms * 10000)
    status, _ = api.call("POST", "/Sessions/Playing", start)
    log("PlaybackStart -> %s" % status)
    seek_wall = None  # monotonic time of the seek, if any
    seeked = False
    while time.monotonic() - t0 < args.play_seconds:
        elapsed = time.monotonic() - t0
        if args.seek_after and not seeked and elapsed >= args.seek_after:
            seeked = True
            seek_wall = time.monotonic()
            api.call("POST", "/Sessions/Playing/Progress", dict(base, PositionTicks=args.seek_to_ms * 10000, EventName="TimeUpdate"))
            log("seek -> %d ms" % args.seek_to_ms)
        if seeked:
            pos_ms = args.seek_to_ms + int((time.monotonic() - seek_wall) * 1000)
        else:
            pos_ms = args.start_ms + int(elapsed * 1000)
        api.call("POST", "/Sessions/Playing/Progress", dict(base, PositionTicks=pos_ms * 10000, EventName="TimeUpdate"))
        time.sleep(3)
        status, st = api.call("GET", "/JellyWled/Status")
        dev = (st or {}).get("devices", [{}])[0]
        jobs = [(j.get("itemName"), j.get("state"), j.get("doneFrames"), j.get("expectedFrames")) for j in (st or {}).get("jobs", [])]
        log("device=%s fps=%s hasFrame=%s pos=%s jobs=%s" % (dev.get("state"), dev.get("fps"), dev.get("hasFrame"), dev.get("positionMs"), jobs))
    api.call("POST", "/Sessions/Playing/Stopped", {"ItemId": item["Id"], "PlaySessionId": play_session, "MediaSourceId": item["Id"], "PositionTicks": 0})
    log("PlaybackStopped sent")
    t_end = time.monotonic()
    time.sleep(2)

    # Evaluate the simulator CSV(s): rows after t0 (unix ms) -> side colors vs schedule. Columns:
    # unix_ms, frame_index, led_count, top r g b, right r g b, bottom r g b, left r g b, led0 r g b
    wall_t0 = time.time() - (t_end - t0) - 2
    wall_seek = None if seek_wall is None else wall_t0 + (seek_wall - t0)
    worst = 0
    for path in [args.csv] + list(args.extra_csv):
        rc = evaluate(path, wall_t0, wall_seek, args)
        worst = max(worst, rc)
    if worst == 0:
        log("PASS")
    return worst


def evaluate(path, wall_t0, wall_seek, args):
    rows = []
    with open(path, newline="") as fh:
        for row in csv.reader(fh):
            if not row or not row[0].isdigit():
                continue
            rows.append([int(float(x)) for x in row])
    rows = [r for r in rows if r[0] >= int(wall_t0 * 1000)]
    if not rows:
        log("FAIL (%s): the simulator received no frames during playback" % path)
        return 1
    span = (rows[-1][0] - rows[0][0]) / 1000.0
    log("%s: %d frames during playback (led count %d, %.1f fps)" % (path, len(rows), rows[-1][2], (len(rows) - 1) / span if span > 0 else 0))
    checked = ok = 0
    first_good = None
    for r in rows:
        wall = r[0] / 1000.0
        if wall_seek is not None and wall >= wall_seek:
            t = (wall - wall_seek) + args.seek_to_ms / 1000.0
            if wall - wall_seek < 1.5:
                continue  # the seek itself takes a report to arrive
        else:
            t = (wall - wall_t0) + args.start_ms / 1000.0
        # Skip the first second after every color change (smoothing and report granularity), and anything past
        # the 60 s fixture (the plugin drops a session that ran past the end).
        if t % 10 < 1.0 or t % 10 > 9.0 or t > 59.0:
            continue
        sides = {"top": r[3:6], "right": r[6:9], "bottom": r[9:12], "left": r[12:15]}
        good = all(close(sides[s], expected_color(s, t), 60) for s in sides)
        checked += 1
        if good:
            ok += 1
            if first_good is None:
                first_good = t
    log("%s: color checks %d/%d frames match the schedule; first match at t=%.1fs" % (path, ok, checked, first_good if first_good is not None else -1))
    if checked == 0 or ok < checked * 0.7:
        log("FAIL (%s): colors did not follow the fixture schedule" % path)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
