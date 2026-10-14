# Jelly-WLED Glow has no reflection-based serialization: JSON goes through the platform's org.json and
# settings through DataStore Preferences, both of which ship their own consumer rules. OkHttp and Okio
# also ship consumer rules. Nothing app-specific needs keeping.
