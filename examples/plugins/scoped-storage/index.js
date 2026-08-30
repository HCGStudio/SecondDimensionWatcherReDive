'use strict';

function relative(path) {
  return String(path || '').replace(/^\/+/, '');
}

globalThis.sdwPlugin = {
  handlers: {
    exists(input) {
      return sdw.request('data.exists', { path: relative(input.path) });
    },
    info(input) {
      return sdw.request('data.info', { path: relative(input.path) });
    },
    read(input) {
      return sdw.request('data.read', { path: relative(input.path) });
    },
    list(input) {
      const base = relative(input.path);
      return sdw.request('data.list', { path: base }).map((entry) => ({
        isDirectory: entry.isDirectory,
        path: base ? `${base}/${entry.name}` : entry.name,
        fileName: entry.name,
        length: entry.length,
        lastModifiedUtc: entry.lastModifiedUtc,
      }));
    },
    seed(input) {
      return sdw.request('data.write', {
        path: relative(input.path),
        base64: input.base64,
      });
    },
  },
};
