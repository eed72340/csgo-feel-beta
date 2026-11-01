"""在 client.dll 里搜 ConVarRef::GetFloat / SetFloat 这类短特征码。

用法: python find_snippets.py <client.dll>
"""
import re
import sys

import pefile

if len(sys.argv) < 2:
    print(__doc__.strip())
    sys.exit(1)

p = sys.argv[1]
pe = pefile.PE(p, fast_load=True)
data = open(p, 'rb').read()
base = pe.OPTIONAL_HEADER.ImageBase
s = [x for x in pe.sections if x.Name.rstrip(b'\0') == b'.text'][0]
blob = data[s.PointerToRawData:s.PointerToRawData + s.SizeOfRawData]
tva = base + s.VirtualAddress

pats = {
    'getname': bytes.fromhex('488B4108488B00C3'),
    'setfloat': bytes.fromhex('488B4108F30F114058C3'),
    'getint': bytes.fromhex('488B41088B4058C3'),
}
for k, v in pats.items():
    hits = [m.start() for m in re.finditer(re.escape(v), blob)]
    print(k, len(hits), [hex(tva + h) for h in hits[:8]])
