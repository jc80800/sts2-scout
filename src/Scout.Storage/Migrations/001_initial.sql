CREATE TABLE IF NOT EXISTS observations(id INTEGER PRIMARY KEY, at TEXT NOT NULL, session TEXT NOT NULL, screen TEXT NOT NULL, confidence REAL NOT NULL, profile_version TEXT NOT NULL, frame_hash TEXT NOT NULL, pack_version TEXT NOT NULL, context_json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS choices(observation_id INTEGER NOT NULL REFERENCES observations(id), slot INTEGER NOT NULL, entity_id TEXT, confidence REAL NOT NULL, price INTEGER, price_confidence REAL NOT NULL, upgraded INTEGER NOT NULL, PRIMARY KEY(observation_id,slot));
CREATE TABLE IF NOT EXISTS selections(id INTEGER PRIMARY KEY, observation_id INTEGER NOT NULL REFERENCES observations(id), slot INTEGER NOT NULL, entity_id TEXT NOT NULL, confidence REAL NOT NULL, evidence TEXT NOT NULL, confirmed INTEGER NOT NULL CHECK(confirmed=0));
CREATE TABLE IF NOT EXISTS provenance(pack_version TEXT PRIMARY KEY, pack_json TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS observation_time ON observations(at);
PRAGMA user_version=1;
