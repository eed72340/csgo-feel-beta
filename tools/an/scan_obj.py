"""找引用指定数据地址（ConVar 对象）的代码。

用法: python scan_obj.py <client.dll> <hex_va> [范围字节]
"""
import sys
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from capstone.x86 import X86_OP_MEM, X86_REG_RIP


def main():
    path = sys.argv[1]
    va = int(sys.argv[2], 16)
    span = int(sys.argv[3], 16) if len(sys.argv) > 3 else 0x80

    pe = pefile.PE(path, fast_load=True)
    pe.parse_data_directories(directories=[
        pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXCEPTION"]])
    data = open(path, "rb").read()
    base = pe.OPTIONAL_HEADER.ImageBase
    funcs = sorted((e.struct.BeginAddress, e.struct.EndAddress)
                   for e in pe.DIRECTORY_ENTRY_EXCEPTION)

    text = [s for s in pe.sections if s.Name.rstrip(b"\0") == b".text"][0]
    code = data[text.PointerToRawData:text.PointerToRawData + text.SizeOfRawData]
    code_va = base + text.VirtualAddress

    lo, hi = va, va + span
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True

    hits = []

    def host_fn(rva):
        for b, e in funcs:
            if b <= rva < e:
                return b, e
        return None

    off = 0
    total = len(code)
    while off < total:
        progressed = False
        last_end = 0
        for insn in md.disasm(code[off:off + 0x10000], code_va + off):
            progressed = True
            last_end = insn.address + insn.size
            for op in insn.operands:
                if op.type != X86_OP_MEM or op.mem.base != X86_REG_RIP:
                    continue
                tgt = insn.address + insn.size + op.mem.disp
                if lo <= tgt < hi:
                    hits.append((insn.address, tgt, insn.mnemonic + " " + insn.op_str))
        off = (last_end - code_va) if progressed else off + 1

    print(f"object 0x{va:X}..0x{hi:X}: {len(hits)} refs")
    for addr, tgt, txt in hits:
        h = host_fn(addr - base)
        ftext = f"func 0x{base + h[0]:X}..0x{base + h[1]:X}" if h else "func ?"
        print(f"  0x{addr:X} (rva 0x{addr-base:X}) -> 0x{tgt:X} ({tgt-va:+#x})  {txt}   [{ftext}]")


if __name__ == "__main__":
    main()
