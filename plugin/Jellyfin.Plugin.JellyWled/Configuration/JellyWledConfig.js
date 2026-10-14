const PLUGIN_ID = '7568fce5-c039-4d52-952f-38487b033df0';

const ANALYSIS_NUM = {
    AnalysisFps: [1, 30, 10], RingWidth: [4, 256, 32], RingHeight: [4, 256, 18], BandPercent: [1, 50, 8],
    AnalysisThreads: [1, 16, 2], MaxConcurrentAnalyses: [1, 8, 1], TaskMaxItemsPerRun: [0, 100000, 50]
};
const BOOLS = ['DetectLetterbox', 'AnalyzeOnPlayback', 'AnalyzeNewItems', 'LowPriority', 'FastDecode',
    'TaskIncludeMovies', 'TaskIncludeEpisodes', 'TaskNewestFirst', 'EnableServerOutput'];

// Device field spec: [key, kind, label, description, extra]. kind: text | int | float | bool | select | multi.
const CORNERS = [['top_left', 'Top left'], ['top_right', 'Top right'], ['bottom_right', 'Bottom right'], ['bottom_left', 'Bottom left']];
const DEVICE_FIELDS = [
    ['Enabled', 'bool', 'Enabled', 'Disabled devices are kept in the list but receive nothing.'],
    ['Name', 'text', 'Name', 'Shown in the status list.', { def: 'Living room TV' }],
    ['Host', 'text', 'Host', 'Hostname or IP address of the WLED node, reachable from this server.', { def: '' }],
    ['DdpPort', 'int', 'DDP port', 'UDP port for the pixel data. WLED uses 4048 by default.', { min: 1, max: 65535, def: 4048 }],
    ['HttpPort', 'int', 'HTTP port', 'Port of the WLED web interface, used to read the node state. Default 80.', { min: 1, max: 65535, def: 80 }],
    ['LedsTop', 'int', 'LEDs top', 'LEDs along the top edge, seen from the front.', { min: 0, max: 2000, def: 60 }],
    ['LedsRight', 'int', 'LEDs right', 'LEDs along the right edge.', { min: 0, max: 2000, def: 34 }],
    ['LedsBottom', 'int', 'LEDs bottom', 'LEDs along the bottom edge.', { min: 0, max: 2000, def: 60 }],
    ['LedsLeft', 'int', 'LEDs left', 'LEDs along the left edge.', { min: 0, max: 2000, def: 34 }],
    ['StartCorner', 'select', 'LED 0 sits at', 'The corner where the first LED of the strip is, seen from the front.', { opts: CORNERS, def: 'top_left' }],
    ['Direction', 'select', 'Strip runs', 'The direction the strip runs from LED 0.', { opts: [['cw', 'Clockwise (seen from the front)'], ['ccw', 'Counterclockwise']], def: 'cw' }],
    ['Mode', 'select', 'Mode', 'How the picture maps to this node.', { opts: [['ring', 'Edges follow the picture'], ['average', 'Whole node shows the average color (lamps, light bars)']], def: 'ring' }],
    ['Brightness', 'int', 'Brightness (%)', '0 to 100.', { min: 0, max: 100, def: 60 }],
    ['Saturation', 'int', 'Color richness (%)', '0 to 300. 100 means colors as in the picture.', { min: 0, max: 300, def: 120 }],
    ['Gamma', 'float', 'Gamma', '0.5 to 4. Higher values make dark colors darker.', { min: 0.5, max: 4, step: 0.1, def: 2.2 }],
    ['Smoothing', 'int', 'Smoothing (%)', '0 to 95. How much of the previous frame is kept; higher is calmer.', { min: 0, max: 95, def: 35 }],
    ['Fps', 'int', 'Frames per second', '1 to 60. How often the strip is updated.', { min: 1, max: 60, def: 25 }],
    ['SyncOffsetMs', 'int', 'Sync offset (ms)', '-5000 to 5000. Positive values make the lights run earlier than the picture.', { min: -5000, max: 5000, def: 0 }],
    ['RestoreState', 'bool', 'Leave live mode when playback stops', 'Restores the previous on/off state and brightness of the node when playback ends.'],
    ['FollowAllSessions', 'bool', 'Follow every session', 'Light up for any playback on this server, whatever device or user.'],
    ['FollowDeviceIds', 'multi', 'Follow these Jellyfin devices', 'Light up when one of these devices plays. Select none to follow no specific device.', { src: 'devices' }],
    ['FollowUserIds', 'multi', 'Follow these users', 'Light up when any device of these users plays.', { src: 'users' }]
];

const esc = (s) => String(s == null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
// Jellyfin serializes MVC output with its own casing; accept both camelCase and PascalCase.
const g = (o, k) => (o == null ? undefined : (o[k] !== undefined ? o[k] : o[k.charAt(0).toUpperCase() + k.slice(1)]));
const clamp = (v, lo, hi, def) => { v = Number(v); return Number.isFinite(v) ? Math.min(hi, Math.max(lo, v)) : def; };
const mmss = (ticks) => { const s = Math.max(0, Math.floor((Number(ticks) || 0) / 1e7)); return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0'); };
const row = (title, text) => `<div class="listItem listItem-border"><div class="listItemBody two-line"><div class="listItemBodyText">${title}</div><div class="listItemBodyText secondary">${text}</div></div></div>`;
const none = (t) => `<p class="fieldDescription">${t}</p>`;

function fieldHtml(uid, f, dev, lists) {
    const [key, kind, label, desc, x = {}] = f;
    const id = `jw-${uid}-${key}`;
    const v = dev[key];
    const d = desc ? `<div class="fieldDescription">${esc(desc)}</div>` : '';
    if (kind === 'bool') {
        return `<div class="checkboxContainer checkboxContainer-withDescription"><label class="emby-checkbox-label"><input id="${id}" data-f="${key}" type="checkbox" is="emby-checkbox"${v ? ' checked' : ''} /><span>${esc(label)}</span></label><div class="fieldDescription checkboxFieldDescription">${esc(desc)}</div></div>`;
    }
    if (kind === 'select') {
        const o = x.opts.map(([val, text]) => `<option value="${val}"${val === v ? ' selected' : ''}>${esc(text)}</option>`).join('');
        return `<div class="selectContainer"><label class="selectLabel" for="${id}">${esc(label)}</label><select is="emby-select" id="${id}" data-f="${key}" class="emby-select-withcolor emby-select">${o}</select>${d}</div>`;
    }
    if (kind === 'multi') {
        const chosen = Array.isArray(v) ? v : [];
        const known = lists[x.src] || [];
        const all = known.concat(chosen.filter((c) => !known.some((k) => k[0] === c)).map((c) => [c, c + ' (not in the current list)']));
        const o = all.map(([val, text]) => `<option value="${esc(val)}"${chosen.includes(val) ? ' selected' : ''}>${esc(text)}</option>`).join('');
        return `<div class="selectContainer"><label class="selectLabel" for="${id}">${esc(label)}</label><select is="emby-select" id="${id}" data-f="${key}" multiple class="emby-select-withcolor emby-select">${o}</select>${d}</div>`;
    }
    const num = kind === 'text' ? '' : ` min="${x.min}" max="${x.max}"${x.step ? ` step="${x.step}"` : ''}`;
    return `<div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="${id}">${esc(label)}</label><input id="${id}" data-f="${key}" type="${kind === 'text' ? 'text' : 'number'}" is="emby-input"${num} value="${esc(v)}" />${d}</div>`;
}

function deviceHtml(dev, lists) {
    const uid = String(dev.Id).replace(/[^a-zA-Z0-9]/g, '');
    const f = (k) => fieldHtml(uid, DEVICE_FIELDS.find((x) => x[0] === k), dev, lists);
    const rows = [
        ['Enabled'], ['Name', 'Host', 'DdpPort', 'HttpPort'], ['LedsTop', 'LedsRight', 'LedsBottom', 'LedsLeft'],
        ['StartCorner', 'Direction', 'Mode'], ['Brightness', 'Saturation', 'Gamma', 'Smoothing', 'Fps', 'SyncOffsetMs'],
        ['RestoreState'], ['FollowAllSessions'], ['FollowDeviceIds', 'FollowUserIds']
    ];
    const body = rows.map((r) => (r.length === 1 ? f(r[0]) : `<div class="jw-row">${r.map(f).join('')}</div>`)).join('');
    return `<div class="jw-device" data-id="${esc(dev.Id)}"><h3>${esc(dev.Name || 'New device')}</h3>${body}
        <div class="jw-buttons">
            <button is="emby-button" type="button" class="raised button emby-button" data-act="probe"><span>Probe</span></button>
            <button is="emby-button" type="button" class="raised button emby-button" data-act="test" data-pattern="sides"><span>Show sides</span></button>
            <button is="emby-button" type="button" class="raised button emby-button" data-act="test" data-pattern="chase"><span>Chase</span></button>
            <button is="emby-button" type="button" class="raised button emby-button" data-act="test" data-pattern="white"><span>White</span></button>
            <button is="emby-button" type="button" class="raised button emby-button" data-act="test" data-pattern="off"><span>Off</span></button>
            <button is="emby-button" type="button" class="raised button emby-button" data-act="remove"><span>Remove</span></button>
        </div>
        <div class="fieldDescription">Probe reads the node info through the server. The test buttons run a pattern for 15 seconds; the device must be saved and enabled first, with server output on.</div>
        <div class="jw-result" data-role="result"></div></div>`;
}

function defaultDevice() {
    const d = { Id: crypto.randomUUID(), FollowDeviceIds: [], FollowUserIds: [], Enabled: true, RestoreState: true, FollowAllSessions: false };
    DEVICE_FIELDS.forEach((f) => { if (f[4] && f[4].def !== undefined) d[f[0]] = f[4].def; });
    return d;
}

function readDevice(el) {
    const dev = { Id: el.dataset.id };
    DEVICE_FIELDS.forEach(([key, kind, , , x = {}]) => {
        const c = el.querySelector(`[data-f="${key}"]`);
        if (kind === 'bool') dev[key] = c.checked;
        else if (kind === 'multi') dev[key] = Array.from(c.selectedOptions).map((o) => o.value);
        else if (kind === 'int') dev[key] = Math.round(clamp(c.value, x.min, x.max, x.def));
        else if (kind === 'float') dev[key] = clamp(c.value, x.min, x.max, x.def);
        else dev[key] = String(c.value || '').trim();
    });
    return dev;
}

export default function (view, params) {
    const $ = (id) => view.querySelector('#' + id);
    const api = () => window.ApiClient;
    let timer = null;
    let busy = false;
    let savedIds = new Set();
    let lists = { devices: [], users: [] };

    function renderStatus(s) {
        const cache = g(s, 'cache') || {};
        const mb = ((Number(g(cache, 'bytes')) || 0) / 1048576).toFixed(1);
        $('jwSummary').innerHTML = `Plugin version ${esc(g(s, 'pluginVersion'))}. Server output is <strong>${g(s, 'serverOutput') ? 'on' : 'off'}</strong>. Track cache: ${esc(g(cache, 'items'))} items, ${mb} MB.`;
        const devs = g(s, 'devices') || [];
        $('jwStatusDevices').innerHTML = devs.length ? devs.map((d) => row(
            `${esc(g(d, 'name'))} (${esc(g(d, 'host'))}): ${esc(g(d, 'state'))}`,
            `Following: ${esc(g(d, 'itemName') || 'nothing')}. ${(Number(g(d, 'fps')) || 0).toFixed(1)} fps, ${esc(g(d, 'ledCount'))} LEDs.${g(d, 'lastError') ? ' Last error: ' + esc(g(d, 'lastError')) : ''}`)).join('') : none('No devices configured.');
        const ses = g(s, 'sessions') || [];
        $('jwStatusSessions').innerHTML = ses.length ? ses.map((x) => {
            const t = g(x, 'track') || {};
            return row(`${esc(g(x, 'deviceName'))} (${esc(g(x, 'client'))}, ${esc(g(x, 'userName'))})`,
                `${esc(g(x, 'itemName'))} at ${mmss(g(x, 'positionTicks'))}${g(x, 'isPaused') ? ', paused' : ''}. Track ${esc(g(t, 'state'))}, ${esc(g(t, 'frames'))}/${esc(g(t, 'expectedFrames'))} frames.`);
        }).join('') : none('No followed sessions.');
        const job = (j) => row(`${esc(g(j, 'itemName'))}: ${esc(g(j, 'state'))}`,
            `${esc(g(j, 'doneFrames'))}/${esc(g(j, 'expectedFrames'))} frames. Reason: ${esc(g(j, 'reason'))}.${g(j, 'error') ? ' Error: ' + esc(g(j, 'error')) : ''}`);
        const jobs = g(s, 'jobs') || [];
        const recent = g(s, 'recentJobs') || [];
        $('jwStatusJobs').innerHTML = jobs.length ? jobs.map(job).join('') : none('No analysis running or queued.');
        $('jwStatusRecent').innerHTML = recent.length ? recent.map(job).join('') : none('Nothing finished yet.');
    }

    function refresh() {
        if (busy || !api()) return;
        busy = true;
        api().getJSON(api().getUrl('JellyWled/Status')).then(renderStatus, () => { $('jwSummary').textContent = 'Could not read the plugin status.'; }).then(() => { busy = false; });
    }

    function addBlock(dev) {
        const wrap = document.createElement('div');
        wrap.innerHTML = deviceHtml(dev, lists);
        $('jwDevices').appendChild(wrap.firstElementChild);
    }

    function fillConfig(cfg) {
        Object.keys(ANALYSIS_NUM).forEach((k) => { $(k).value = cfg[k] !== undefined ? cfg[k] : ANALYSIS_NUM[k][2]; });
        BOOLS.forEach((k) => { $(k).checked = cfg[k] !== undefined ? !!cfg[k] : $(k).defaultChecked; });
        $('CacheDirectory').value = cfg.CacheDirectory || '';
        const sel = Array.isArray(cfg.TaskLibraryIds) ? cfg.TaskLibraryIds : [];
        const libs = lists.libraries.concat(sel.filter((i) => !lists.libraries.some((l) => l[0] === i)).map((i) => [i, i + ' (not in the current list)']));
        $('TaskLibraryIds').innerHTML = libs.map(([v, t]) => `<option value="${esc(v)}"${sel.includes(v) ? ' selected' : ''}>${esc(t)}</option>`).join('');
        const devs = Array.isArray(cfg.Devices) ? cfg.Devices : [];
        savedIds = new Set(devs.map((d) => String(d.Id)));
        $('jwDevices').innerHTML = '';
        devs.forEach((d) => addBlock(Object.assign(defaultDevice(), d)));
    }

    function load() {
        const a = api();
        window.Dashboard.showLoadingMsg();
        const safe = (p) => Promise.resolve(p).catch(() => null);
        Promise.all([
            a.getPluginConfiguration(PLUGIN_ID),
            safe(a.getJSON(a.getUrl('Devices'))),
            safe(a.getUsers()),
            safe(a.getJSON(a.getUrl('Library/VirtualFolders')))
        ]).then(([cfg, devices, users, folders]) => {
            const items = Array.isArray(devices) ? devices : ((devices && (devices.Items || devices.items)) || []);
            lists = {
                devices: items.map((d) => [d.Id, [d.Name, d.AppName, d.LastUserName].filter(Boolean).join(' / ')]),
                users: (Array.isArray(users) ? users : []).map((u) => [u.Id, u.Name]),
                libraries: (Array.isArray(folders) ? folders : []).map((f) => [f.ItemId, f.Name])
            };
            fillConfig(cfg);
        }).catch(() => window.Dashboard.alert('Could not load the Jelly-WLED configuration.')).then(() => window.Dashboard.hideLoadingMsg());
    }

    function showResult(block, html) { block.querySelector('[data-role="result"]').innerHTML = html; }

    function onDeviceClick(e) {
        const btn = e.target.closest('button[data-act]');
        if (!btn) return;
        const block = btn.closest('.jw-device');
        const id = block.dataset.id;
        const act = btn.dataset.act;
        const a = api();
        if (act === 'remove') {
            if (window.confirm('Remove this device? The change is applied when you save.')) block.remove();
        } else if (act === 'probe') {
            const dev = readDevice(block);
            const url = savedIds.has(id) ? a.getUrl('JellyWled/Devices/' + id + '/Probe') : a.getUrl('JellyWled/Probe', { host: dev.Host, httpPort: dev.HttpPort });
            showResult(block, 'Probing...');
            a.getJSON(url).then((r) => {
                showResult(block, g(r, 'ok') === false
                    ? `Probe failed: ${esc(g(r, 'error'))}`
                    : `WLED ${esc(g(r, 'version'))}, name ${esc(g(r, 'name'))}, ${esc(g(r, 'ledCount'))} LEDs, live mode ${g(r, 'live') ? 'on' : 'off'}.`);
            }, () => showResult(block, 'Probe failed. Enter a host, or save the device first.'));
        } else if (act === 'test') {
            const p = btn.dataset.pattern;
            a.ajax({ type: 'POST', url: a.getUrl('JellyWled/Devices/' + id + '/Test', { pattern: p, seconds: 15 }) })
                .then(() => showResult(block, `Test pattern "${esc(p)}" started.`),
                    () => showResult(block, 'The test could not start. Save the device first; it must be enabled, have a host, and server output must be on.'));
        }
    }

    function save(e) {
        e.preventDefault();
        const a = api();
        window.Dashboard.showLoadingMsg();
        a.getPluginConfiguration(PLUGIN_ID).then((cfg) => {
            Object.keys(ANALYSIS_NUM).forEach((k) => { const [lo, hi, def] = ANALYSIS_NUM[k]; cfg[k] = Math.round(clamp($(k).value, lo, hi, def)); });
            BOOLS.forEach((k) => { cfg[k] = $(k).checked; });
            cfg.CacheDirectory = $('CacheDirectory').value.trim();
            cfg.TaskLibraryIds = Array.from($('TaskLibraryIds').selectedOptions).map((o) => o.value);
            cfg.Devices = Array.from(view.querySelectorAll('.jw-device')).map(readDevice);
            return a.updatePluginConfiguration(PLUGIN_ID, cfg).then((r) => {
                savedIds = new Set(cfg.Devices.map((d) => d.Id));
                window.Dashboard.processPluginConfigurationUpdateResult(r);
            });
        }).catch(() => { window.Dashboard.hideLoadingMsg(); window.Dashboard.alert('Saving failed.'); });
        return false;
    }

    $('JellyWledConfigForm').addEventListener('submit', save);
    $('jwDevices').addEventListener('click', onDeviceClick);
    $('jwAddDevice').addEventListener('click', () => addBlock(defaultDevice()));
    $('jwDeleteTrack').addEventListener('click', () => {
        const id = $('jwDeleteId').value.trim();
        if (!id) return;
        const a = api();
        a.ajax({ type: 'DELETE', url: a.getUrl('JellyWled/Track/' + encodeURIComponent(id)) })
            .then(() => { $('jwDeleteResult').textContent = 'Track deleted.'; refresh(); }, () => { $('jwDeleteResult').textContent = 'Delete failed. Check the item id.'; });
    });

    view.addEventListener('viewshow', () => {
        load();
        refresh();
        clearInterval(timer);
        timer = setInterval(refresh, 3000);
    });
    const stop = () => { clearInterval(timer); timer = null; };
    view.addEventListener('viewhide', stop);
    view.addEventListener('viewdestroy', stop);
}
