"""按 .pdata 函数边界反汇编，定位 ConVar 对象与函数入口。

用法: python func_at.py <client.dll> <hex_rva> [窗口字节数]
"""
import sys
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from capstone.x86 import X86_OP_MEM, X86_REG_RIP, X86_OP_IMM


def load(path):
    pe = pefile.PE(path, fast_load=True)
    pe.parse_data_directories(directories=[
        pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXCEPTION"]])
    data = open(path, "rb").read()
    return pe, data


def rva_to_off(pe, rva):
    for s in pe.sections:
        if s.VirtualAddress <= rva < s.VirtualAddress + max(s.Misc_VirtualSize, s.SizeOfRawData):
            return s.PointerToRawData + (rva - s.VirtualAddress)
    return None


def functions(pe):
    out = []
    if not hasattr(pe, "DIRECTORY_ENTRY_EXCEPTION"):
        return out
    for entry in pe.DIRECTORY_ENTRY_EXCEPTION:
        try:
            out.append((entry.struct.BeginAddress, entry.struct.EndAddress))
        except AttributeError:
            pass
    return sorted(out)


def main():
    path = sys.argv[1]
    rva = int(sys.argv[2], 16)
    window = int(sys.argv[3], 0) if len(sys.argv) > 3 else 0x200

    pe, data = load(path)
    base = pe.OPTIONAL_HEADER.ImageBase
    funcs = functions(pe)
    print(f"pdata functions: {len(funcs)}")

    host = None
    for b, e in funcs:
        if b <= rva < e:
            host = (b, e)
            break
    print(f"target rva 0x{rva:X} in function " + (f"0x{host[0]:X}..0x{host[1]:X}" if host else "NOT FOUND"))

    start = host[0] if host else (rva - 0x40)
    off = rva_to_off(pe, start)
    size = (host[1] - host[0]) if host else window
    size = min(size, window)
    code = data[off:off + size]

    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True
    for insn in md.disasm(code, base + start):
        mark = ">>" if insn.address <= base + rva < insn.address + insn.size else "  "
        extra = ""
        for op in insn.operands:
            if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                tgt = insn.address + insn.size + op.mem.disp
                r = tgt - base
                where = "?"
                if 0 <= r < pe.OPTIONAL_HEADER.SizeOfImage:
                    for s in pe.sections:
                        if s.VirtualAddress <= r < s.VirtualAddress + max(s.Misc_VirtualSize, s.SizeOfRawData):
                            where = s.Name.rstrip(b"\0").decode()
                            break
                extra += f"   ; -> 0x{tgt:X} ({where})"
        print(f"  {mark} 0x{insn.address:X} (rva 0x{insn.address-base:X}): {insn.mnemonic:<8} {insn.op_str}{extra}")

    # 找函数内 lea rcx, [rip+X] / lea rdx, [rip+X]，其目标在数据节 → 很可能是 ConVar 对象
    print("\n-- data refs in function --")
    for insn in md.disasm(code, base + start):
        if insn.mnemonic != "lea":
            continue
        for op in insn.operands:
            if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                tgt = insn.address + insn.size + op.mem.disp
                r = tgt - base
                if 0 <= r < pe.OPTIONAL_HEADER.SizeOfImage:
                    for s in pe.sections:
                        if s.VirtualAddress <= r < s.VirtualAddress + max(s.Misc_VirtualSize, s.SizeOfRawData):
                            name = s.Name.rstrip(b"\0").decode()
                            if name != ".text":
                                print(f"  0x{insn.address:X}: {insn.mnemonic} {insn.op_str} -> 0x{tgt:X} ({name})")
                            break


if __name__ == "__main__":
    main()
