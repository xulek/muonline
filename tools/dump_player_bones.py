import struct
import sys

def read_cstr(data, off, length=32):
    raw = data[off:off + length]
    end = raw.find(b'\x00')
    if end < 0:
        end = length
    return raw[:end].decode('ascii', errors='replace'), off + length

def dump_bones(path):
    with open(path, 'rb') as f:
        data = f.read()

    if data[:3] != b'BMD':
        print('Not a BMD file')
        return

    version = data[3]
    print(f'version = {version}, size = {len(data)}')

    if version == 12:
        size = struct.unpack_from('<i', data, 4)[0]
        enc = bytearray(data[8:8 + size])
        xor_key = bytes([0xD1, 0x73, 0x52, 0xF6, 0xD2, 0x9A, 0xCB, 0x27,
                         0x3E, 0xAF, 0x59, 0x31, 0x37, 0xB3, 0xE7, 0xA2])
        map_key = 0x5E
        dec = bytearray(size)
        for i in range(size):
            dec[i] = (enc[i] ^ xor_key[i % 16]) - map_key & 0xFF
            map_key = (enc[i] + 0x3D) & 0xFF
        data = data[:4] + bytes(dec) + data[8 + size:]
    elif version == 15:
        print('Encrypted BMD (LEA) - cannot parse without decryption.')
        return

    off = 4
    name, off = read_cstr(data, off)
    mesh_count, bone_count, action_count = struct.unpack_from('<HHH', data, off)
    off += 6
    print(f'model={name!r} meshes={mesh_count} bones={bone_count} actions={action_count}')

    # Skip meshes
    for _ in range(mesh_count):
        num_v, num_n, num_t, num_tri, tex = struct.unpack_from('<hhhhh', data, off)
        off += 10
        off += 14 * num_v      # BMDTextureVertex: short + Vector3
        off += 16 * num_n      # BMDTextureNormal: short + Vector3 + short
        off += 8 * num_t       # BMDTexCoord: float, float
        off += 59 * num_tri    # BMDTriangle
        _, off = read_cstr(data, off)  # texture path

    # Read actions
    actions = []
    for _ in range(action_count):
        num_keys, lock_pos = struct.unpack_from('<h?', data, off)
        off += 3
        if lock_pos:
            off += 12 * num_keys
        actions.append((num_keys, lock_pos))

    # Read bones
    bones = []
    for i in range(bone_count):
        dummy = struct.unpack_from('<?', data, off)[0]
        off += 1
        if dummy:
            bones.append((i, '<DUMMY>', -1))
            continue
        bname, off = read_cstr(data, off)
        parent = struct.unpack_from('<h', data, off)[0]
        off += 2
        for (num_keys, _) in actions:
            off += 12 * num_keys  # positions
            off += 12 * num_keys  # rotations
        bones.append((i, bname, parent))

    print('\nindex  name                                  parent')
    for i, bname, parent in bones:
        print(f'{i:5d}  {bname:35s} {parent}')

if __name__ == '__main__':
    dump_bones(sys.argv[1] if len(sys.argv) > 1 else r'D:\nowe\muonline\SourceMain5.2\bin\Data\Player\player.bmd')
