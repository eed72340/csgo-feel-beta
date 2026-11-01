"""找调用某函数的 E8 rel32 位置（以及跳转）。

用法: python callers.py <client.dll> <hex_rva>
"""
import sys
import struct
import pefile


def main():
    path = sys.argv[1]
    rva = int(sys.argv[2], 16)

    pe = pefile.PE(path, fast_load=True)
    pe.parse_data_directories(directories=[
        pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXCEPTION"]])
    data = open(path, "rb").read()
    base = pe.OPTIONAL_HEADER.ImageBase
    funcs = sorted((e.struct.BeginAddress, e.struct.EndAddress)
                   for e in pe.DIRECTORY_ENTRY_EXCEPTION)

    text = [s for s in pe.sections if s.Name.rstrip(b"\0") == b".text"][0]
    start = text.VirtualAddress
    end = start + text.SizeOfRawData
    blob = data[text.PointerToRawData:text.PointerToRawData + text.SizeOfRawData]

    def host(r):
        for b, e in funcs:
            if b <= r < e:
                return b, e
        return None

    hits = []
    target = base + rva
    for i in range(0, len(blob) - 5):
        if blob[i] != 0xE8:
            continue
        rel = struct.unpack_from("<i", blob, i + 1)[0]
        dst = base + start + i + 5 + rel
        if dst == target:
            call_rva = start + i
            hits.append(call_rva)

    print(f"callers of 0x{target:X}: {len(hits)}")
    for h in hits:
        f = host(h)
        print(f"  call at rva 0x{h:X}" + (f"  in func 0x{f[0]:X}..0x{f[1]:X}" if f else "  (no pdata)"))


if __name__ == "__main__":
    main()
