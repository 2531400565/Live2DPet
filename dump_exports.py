import sys, struct

def u16(d, o): return struct.unpack_from('<H', d, o)[0]
def u32(d, o): return struct.unpack_from('<I', d, o)[0]

def find_exports(path):
    with open(path,'rb') as f:
        data = f.read()
    if data[:2] != b'MZ':
        print("NOT_PE"); return []
    e_lfanew = u32(data, 0x3C)
    if data[e_lfanew:e_lfanew+4] != b'PE\x00\x00':
        print("NO_PE_SIG"); return []
    coff = e_lfanew + 4
    opt = coff + 20
    magic = u16(data, opt)
    opt_size = u16(data, coff+16)          # SizeOfOptionalHeader lives in COFF header
    num_sec = u16(data, coff+2)
    if magic == 0x20b:   # PE32+
        dd = opt + 112
    else:                # PE32
        dd = opt + 96
    export_rva = u32(data, dd)
    if export_rva == 0:
        print("NO_EXPORT_DIR"); return []
    sec = opt + opt_size
    def rva_to_off(rva):
        for i in range(num_sec):
            sh = sec + i*40
            vsize, vaddr, rawsize, rawptr = struct.unpack_from('<IIII', data, sh+8)
            if vaddr <= rva < vaddr + vsize:
                return rawptr + (rva - vaddr)
        return None
    ed = rva_to_off(export_rva)
    if ed is None:
        print("EXPORT_RVA_UNMAPPED"); return []
    num_names = u32(data, ed+24)
    addr_names = u32(data, ed+32)
    addr_ordinals = u32(data, ed+36)
    noff = rva_to_off(addr_names)
    ooff = rva_to_off(addr_ordinals)
    names = []
    for i in range(num_names):
        ent_rva = u32(data, noff + i*4)
        name_off = rva_to_off(ent_rva)
        if name_off is None:
            continue
        end = data.index(b'\x00', name_off)
        names.append(data[name_off:end].decode('ascii','replace'))
    return sorted(names)

if __name__ == '__main__':
    for p in sys.argv[1:]:
        print("=== " + p + " ===")
        names = find_exports(p)
        print("TOTAL exports:", len(names))
        print("--- exports mentioning drawable / renderorder / order ---")
        for n in names:
            if 'Drawable' in n or 'RenderOrder' in n or 'Render' in n or 'Order' in n:
                print(n)
        print("--- all csm* (count) ---", sum(1 for n in names if n.startswith('csm')))
