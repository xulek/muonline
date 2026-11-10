"""Analyze player.bmd bones and Imp shoulder offset targets."""
import struct
import sys
import math


def read_cstr(data, off, length=32):
    raw = data[off:off + length]
    end = raw.find(b'\x00')
    if end < 0:
        end = length
    return raw[:end].decode('ascii', errors='replace'), off + length


def euler_to_matrix(r):
    hx, hy, hz = (math.radians(a) * 0.5 for a in r)
    sx, cx = math.sin(hx), math.cos(hx)
    sy, cy = math.sin(hy), math.cos(hy)
    sz, cz = math.sin(hz), math.cos(hz)
    w = cx * cy * cz + sx * sy * sz
    x = sx * cy * cz - cx * sy * sz
    y = cx * sy * cz + sx * cy * sz
    z = cx * cy * sz - sx * sy * cz
    xx, yy, zz = x * x, y * y, z * z
    xy, xz, yz = x * y, x * z, y * z
    wx, wy, wz = w * x, w * y, w * z
    # XNA/MonoGame Matrix.CreateFromQuaternion (row-vector convention)
    return [
        [1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy)],
        [2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx)],
        [2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy)],
    ]


def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def transform_rot(m, v):
    return [m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
            m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
            m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2]]


def parse(path):
    with open(path, 'rb') as f:
        data = f.read()

    version = data[3]
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
        print('LEA-encrypted; cannot parse.')
        return None

    off = 4
    name, off = read_cstr(data, off)
    mesh_count, bone_count, action_count = struct.unpack_from('<HHH', data, off)
    off += 6
    print(f'model={name!r} meshes={mesh_count} bones={bone_count} actions={action_count}')

    for _ in range(mesh_count):
        num_v, num_n, num_t, num_tri, tex = struct.unpack_from('<hhhhh', data, off)
        off += 10
        off += 14 * num_v
        off += 16 * num_n
        off += 8 * num_t
        off += 59 * num_tri
        _, off = read_cstr(data, off)

    actions = []
    for _ in range(action_count):
        num_keys, lock_pos = struct.unpack_from('<h?', data, off)
        off += 3
        if lock_pos:
            off += 12 * num_keys
        actions.append((num_keys, lock_pos))

    bones = []
    for i in range(bone_count):
        dummy = struct.unpack_from('<?', data, off)[0]
        off += 1
        if dummy:
            bones.append({'index': i, 'name': '<DUMMY>', 'parent': -1})
            continue
        bname, off = read_cstr(data, off)
        parent = struct.unpack_from('<h', data, off)[0]
        off += 2
        per_action = []
        for (num_keys, _) in actions:
            pos = struct.unpack_from('<%df' % (3 * num_keys), data, off)
            off += 12 * num_keys
            rot = struct.unpack_from('<%df' % (3 * num_keys), data, off)
            off += 12 * num_keys
            per_action.append((num_keys, pos, rot))
        bones.append({'index': i, 'name': bname, 'parent': parent, 'actions': per_action})

    return bones, actions


def main(path):
    result = parse(path)
    if result is None:
        return
    bones, actions = result

    print('\nindex  name                                  parent')
    for b in bones:
        print(f"{b['index']:5d}  {b['name']:35s} {b['parent']}")

    n = len(bones)
    abs_rot = [None] * n
    abs_pos = [None] * n
    for b in bones:
        if b['name'] == '<DUMMY>':
            continue
        num_keys, pos, rot = b['actions'][0]
        local = euler_to_matrix((rot[0], rot[1], rot[2]))
        p = (pos[0], pos[1], pos[2])
        parent = b['parent']
        if parent >= 0 and abs_rot[parent] is not None:
            abs_rot[b['index']] = mat_mul(local, abs_rot[parent])
            abs_pos[b['index']] = transform_rot(local, abs_pos[parent])
            abs_pos[b['index']] = [abs_pos[b['index']][k] + p[k] for k in range(3)]
        else:
            abs_rot[b['index']] = local
            abs_pos[b['index']] = list(p)

    print('\n--- bind-pose (action 0, frame 0) ---')
    for idx in [0, 2, 17, 19, 20, 25, 26, 28, 33, 34, 35, 37, 42, 47]:
        if abs_rot[idx] is None:
            continue
        name = bones[idx]['name']
        pos = abs_pos[idx]
        axis_x = transform_rot(abs_rot[idx], (1, 0, 0))
        axis_y = transform_rot(abs_rot[idx], (0, 1, 0))
        axis_z = transform_rot(abs_rot[idx], (0, 0, 1))
        print(f"bone {idx:2d} {name:22s} pos=({pos[0]:7.1f},{pos[1]:7.1f},{pos[2]:7.1f})")
        print(f"     localX=({axis_x[0]:+.2f},{axis_x[1]:+.2f},{axis_x[2]:+.2f}) "
              f"localY=({axis_y[0]:+.2f},{axis_y[1]:+.2f},{axis_y[2]:+.2f}) "
              f"localZ=({axis_z[0]:+.2f},{axis_z[1]:+.2f},{axis_z[2]:+.2f})")

    print('\n--- Imp offset candidates (bone world pos + offset in bone-local axes) ---')
    for idx, label in [(25, 'R Clavicle (25)'), (34, 'L Clavicle (34)')]:
        if abs_rot[idx] is None or abs_pos[idx] is None:
            continue
        for off in [(20, 0, 0), (0, 20, 0), (0, 0, 20), (0, 0, -20), (10, 0, 0), (15, 0, 20)]:
            target = [abs_pos[idx][k] +
                      off[0] * abs_rot[idx][0][k] +
                      off[1] * abs_rot[idx][1][k] +
                      off[2] * abs_rot[idx][2][k] for k in range(3)]
            print(f'{label} off={off}: {tuple(round(v, 1) for v in target)}')


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else r'D:\nowe\muonline\SourceMain5.2\bin\Data\Player\player.bmd')
