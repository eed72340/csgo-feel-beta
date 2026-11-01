"""离线分析 client.dll：定位 viewmodel 相关 ConVar 与引用它们的代码。

用法:
    python scan_xref.py <client.dll> [字符串...]

输出:
    - 字符串 VA
    - 引用该字符串的指令（通常是 ConVar 注册处）
    - 引用该字符串附近 0x40 字节内对象的代码（ConVar 对象被读取处）
"""
import sys
import struct
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from capstone.x86 import X86_OP_MEM, X86_REG_RIP

INTEREST = [
    "viewmodel_offset_x",
    "viewmodel_offset_y",
    "viewmodel_offset_z",
    "viewmodel_fov",
    "viewmodel_presetpos",
    "view_punch_decay",
    "cl_crosshair_recoil",
    "viewmodel_lefthanded",
]


def sections(pe):
    for s in pe.sections:
        name = s.Name.rstrip(b"\0").decode("latin1")
        yield name, s.VirtualAddress, s.Misc_VirtualSize, s.PointerToRawData, s.SizeOfRawData, s.Characteristics


def read_va(data, pe, va, size):
    rva = va - pe.OPTIONAL_HEADER.ImageBase
    for name, vaddr, vsize, praw, rawsize, _ in sections(pe):
        if vaddr <= rva < vaddr + max(vsize, rawsize):
            off = praw + (rva - vaddr)
            return data[off:off + size]
    return b""


def find_string(data, pe, text):
    needle = text.encode() + b"\0"
    base = pe.OPTIONAL_HEADER.ImageBase
    start = 0
    hits = []
    while True:
        i = data.find(needle, start)
        if i < 0:
            break
        # 把文件偏移换算成 VA
        for name, vaddr, vsize, praw, rawsize, _ in sections(pe):
            if praw <= i < praw + rawsize:
                hits.append(base + vaddr + (i - praw))
                break
        start = i + 1
    return hits


def main():
    path = sys.argv[1]
    wanted = sys.argv[2:] or INTEREST

    pe = pefile.PE(path, fast_load=True)
    data = open(path, "rb").read()
    base = pe.OPTIONAL_HEADER.ImageBase
    text = [s for s in sections(pe) if s[0] == ".text"][0]
    print(f"imagebase=0x{base:X}  .text va=0x{text[1]:X} size=0x{text[2]:X}")

    string_vas = {}
    for w in wanted:
        vas = find_string(data, pe, w)
        string_vas[w] = vas
        print(f'string "{w}": ' + (", ".join(f"0x{v:X}" for v in vas) if vas else "MISS"))

    # 线性反汇编 .text，记录所有 RIP 相对引用的目标
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True
    _, vaddr, vsize, praw, rawsize, _ = text
    code = data[praw:praw + rawsize]
    code_va = base + vaddr

    wanted_targets = {va: w for w, vas in string_vas.items() for va in vas}

    refs = []
    count = 0
    off = 0
    total = len(code)
    # 线性反汇编遇到数据空洞会停下，这里按字节滑动继续
    while off < total:
        progressed = False
        for insn in md.disasm(code[off:off + 0x10000], code_va + off):
            progressed = True
            count += 1
            for op in insn.operands:
                if op.type != X86_OP_MEM or op.mem.base != X86_REG_RIP:
                    continue
                target = insn.address + insn.size + op.mem.disp
                if target in wanted_targets:
                    refs.append((target, insn.address, insn.mnemonic + " " + insn.op_str))
        off += 1 if not progressed else insn.address + insn.size - code_va - off
    print(f"disassembled {count} instructions, {len(refs)} string refs")

    for target, addr, text2 in refs:
        name = wanted_targets[target]
        print(f"\n=== {name}: ref at 0x{addr:X} (rva 0x{addr-base:X}) -> 0x{target:X}")
        print(f"    {text2}")
        # 打印该指令前后若干指令，便于看注册调用
        window_start = addr - 0x30
        for insn in md.disasm(read_va(data, pe, window_start, 0x80), window_start):
            mark = ">>" if insn.address == addr else "  "
            print(f"  {mark} 0x{insn.address:X}: {insn.mnemonic:<8} {insn.op_str}")


if __name__ == "__main__":
    main()
