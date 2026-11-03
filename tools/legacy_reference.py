"""Read-only Source VPK inventory and weapon metadata, not native decompilation.
No game binaries are executed or modified; source asset contents are not republished.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import zlib


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def inventory(directory):
    with directory.open('rb') as f:
        magic, version, tree_size = struct.unpack('<III', f.read(12))
        if magic != 0x55AA1234 or version not in (1, 2):
            raise ValueError('Not a supported Source VPK')
        header_size = 12 if version == 1 else 28
        f.seek(header_size)
        tree = f.read(tree_size)
    pos = 0
    def string():
        nonlocal pos
        end = tree.index(b'\0', pos)
        s = tree[pos:end].decode('utf-8')
        pos = end + 1
        return s
    entries = []
    while True:
        ext = string()
        if not ext:
            break
        while True:
            folder = string()
            if not folder:
                break
            while True:
                name = string()
                if not name:
                    break
                crc, preload_size, archive, offset, size, terminator = struct.unpack_from('<IHHIIH', tree, pos)
                pos += 18
                if terminator != 0xFFFF:
                    raise ValueError('Invalid VPK entry terminator')
                preload = tree[pos:pos + preload_size]
                pos += preload_size
                filename = ('' if folder == ' ' else folder + '/') + name + ('' if ext == ' ' else '.' + ext)
                entries.append((filename, crc, archive, offset, size, preload))
    return entries, header_size + tree_size


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--legacy', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    root, out = args.legacy, args.output
    directory = root / 'csgo/pak01_dir.vpk'
    entries, embedded_start = inventory(directory)
    result = {'kind': 'local-file-and-vpk-observation', 'native_decompilation': False,
              'legacy': str(root), 'files': [], 'config': {}, 'weapons': [],
              'limitations': ['Asset fields are not runtime behavior.',
                              'No native movement/bob/recoil algorithm was recovered.',
                              'This report does not prove CS2 parity or one-display-frame latency.']}
    for relative in ['csgo/cfg/default.cfg', 'csgo/steam.inf', 'csgo/bin/client.dll', 'csgo/bin/server.dll', 'csgo/pak01_dir.vpk']:
        path = root / relative
        if path.is_file():
            result['files'].append({'path': relative, 'size': path.stat().st_size, 'sha256': digest(path)})
    config = (root / 'csgo/cfg/default.cfg').read_text(encoding='utf-8-sig')
    for key, value in re.findall(r'^\s*(cl_(?:bob\w*|viewmodel_shift_\w+))\s+([-\d.]+)', config, re.M):
        result['config'][key] = float(value)
    result['steam_inf'] = (root / 'csgo/steam.inf').read_text(encoding='utf-8-sig').strip()
    # Preserve literal numeric fields with names; do not guess missing defaults.
    wanted = re.compile(r'(speed|cycle|recoil|spread|inaccuracy|recovery|deploy|reload|damage|armor|penetration|range|zoom|flinch)', re.I)
    for filename, crc, archive, offset, size, preload in entries:
        if not re.fullmatch(r'scripts/weapon_[^/]+\.txt', filename):
            continue
        source = directory if archive == 0x7FFF else directory.with_name(f'pak01_{archive:03}.vpk')
        with source.open('rb') as f:
            f.seek(offset + (embedded_start if archive == 0x7FFF else 0))
            payload = preload + f.read(size)
        if zlib.crc32(payload) & 0xFFFFFFFF != crc:
            raise ValueError('CRC mismatch: ' + filename)
        text = payload.decode('utf-8-sig', errors='replace')
        fields = {}
        for key, value in re.findall(r'"([^"\r\n]+)"\s+"([^"\r\n]+)"', text):
            if wanted.search(key) and re.fullmatch(r'[-+\d.eE ]+', value):
                fields.setdefault(key, []).append(value)
        result['weapons'].append({'path': filename, 'archive': archive, 'crc32': f'{crc:08x}',
                                  'sha256': hashlib.sha256(payload).hexdigest(), 'fields': fields})
    result['weapon_count'] = len(result['weapons'])
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'PASS: {len(entries)} VPK entries; {len(result["weapons"])} CRC-verified weapon scripts; {len(result["config"])} local bob parameters -> {out}')


if __name__ == '__main__':
    main()
