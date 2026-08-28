import sys, struct

def u16(d, o): return struct.unpack_from('<H', d, o)[0]
def u32(d, o): return struct.unpack_from('<I', d, o)[0]

def diag(path):
    with open(path,'rb') as f:
        data = f.read()
    print("=== " + path + " ===")
    print("size", len(data), "sig", data[:2])
    e_lfanew = u32(data, 0x3C)
    print("e_lfanew", hex(e_lfanew))
    print("pe_sig", data[e_lfanew:e_lfanew+4])
    coff = e_lfanew + 4
    print("machine", hex(u16(data, coff)))
    print("num_sec", u16(data, coff+2))
    opt = coff + 20
    magic = u16(data, opt)
    print("opt magic", hex(magic))
    opt_size = u16(data, opt+16)
    print("opt_size", opt_size)
    # NumberOfRvaAndSizes
    nrd = u32(data, opt+16+opt_size-4) if False else None
    # data dirs start
    if magic == 0x20b:
        dd = opt + 104
    else:
        dd = opt + 96
    print("data dir start", hex(dd))
    nrvas = u32(data, opt+opt_size-4)
    print("NumberOfRvaAndSizes", nrvas)
    for i in range(min(nrvas, 16)):
        rva, size = struct.unpack_from('<II', data, dd + i*8)
        print(f"  dir[{i}] rva={hex(rva)} size={hex(size)}")

for p in sys.argv[1:]:
    diag(p)
