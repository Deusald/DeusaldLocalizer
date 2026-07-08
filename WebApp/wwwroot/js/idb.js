// IndexedDB-backed "folder of files" store for the web client.
//
// Every project file is one record in a single object store, keyed by
// `<location><path>` where <location> is the project's namespace handle and <path> is the
// '/'-separated relative path (e.g. "Keys/abc.json").  (unit separator) can never appear in a
// location or path, so it cleanly delimits the two without clashing with '/'.
//
// This mirrors the disc "folder of files": a single put/delete is atomic (one IndexedDB transaction),
// so no temp-file-rename dance is needed. Eviction is all-or-nothing per origin, and persist() asks the
// browser to make the storage durable so pending/offline work is not silently reclaimed.

const DB_NAME = 'DeusaldLocalizer';
const STORE = 'files';
const SEP = '|'; // never appears in a GUID location handle or a JSON file path

let _dbPromise = null;

function openDb() {
    if (_dbPromise) return _dbPromise;
    _dbPromise = new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, 1);
        req.onupgradeneeded = () => {
            const db = req.result;
            if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE);
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
    return _dbPromise;
}

function tx(db, mode) {
    return db.transaction(STORE, mode).objectStore(STORE);
}

function reqToPromise(request) {
    return new Promise((resolve, reject) => {
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

function key(location, path) {
    return location + SEP + path;
}

export async function get(location, path) {
    const db = await openDb();
    const result = await reqToPromise(tx(db, 'readonly').get(key(location, path)));
    return result === undefined ? null : result;
}

export async function exists(location, path) {
    const db = await openDb();
    // getKey returns the primary key if present, undefined otherwise — cheaper than fetching the value.
    const found = await reqToPromise(tx(db, 'readonly').getKey(key(location, path)));
    return found !== undefined;
}

export async function put(location, path, content) {
    const db = await openDb();
    await reqToPromise(tx(db, 'readwrite').put(content, key(location, path)));
}

export async function del(location, path) {
    const db = await openDb();
    await reqToPromise(tx(db, 'readwrite').delete(key(location, path)));
}

// Returns the leaf names of *.json files directly inside <folder> for this location.
export async function listJson(location, folder) {
    const db = await openDb();
    const prefix = key(location, folder) + '/';
    const keys = await reqToPromise(tx(db, 'readonly').getAllKeys(IDBKeyRange.lowerBound(prefix)));
    const result = [];
    for (const k of keys) {
        if (!k.startsWith(prefix)) break; // keys are sorted; past the prefix range means done
        const leaf = k.substring(prefix.length);
        if (leaf.indexOf('/') === -1 && leaf.endsWith('.json')) result.push(leaf);
    }
    return result;
}

// Returns every file path (relative to the location) stored for a location — used to zip a whole project.
export async function listAll(location) {
    const db = await openDb();
    const prefix = location + SEP;
    const keys = await reqToPromise(tx(db, 'readonly').getAllKeys(IDBKeyRange.lowerBound(prefix)));
    const result = [];
    for (const k of keys) {
        if (!k.startsWith(prefix)) break;
        result.push(k.substring(prefix.length));
    }
    return result;
}

// Returns the distinct location handles that currently have any file stored (used to list projects).
export async function listLocations() {
    const db = await openDb();
    const keys = await reqToPromise(tx(db, 'readonly').getAllKeys());
    const set = new Set();
    for (const k of keys) {
        const i = k.indexOf(SEP);
        if (i > 0) set.add(k.substring(0, i));
    }
    return Array.from(set);
}

// Deletes every file belonging to a location (whole-project delete).
export async function deleteLocation(location) {
    const db = await openDb();
    const store = tx(db, 'readwrite');
    const range = IDBKeyRange.lowerBound(location + SEP);
    const keys = await reqToPromise(store.getAllKeys(range));
    for (const k of keys) {
        if (!k.startsWith(location + SEP)) break;
        await reqToPromise(store.delete(k));
    }
}

// Asks the browser to make this origin's storage durable (not evicted under pressure). Returns bool.
export async function persist() {
    if (navigator.storage && navigator.storage.persist) {
        try { return await navigator.storage.persist(); } catch { return false; }
    }
    return false;
}
