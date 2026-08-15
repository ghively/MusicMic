"""Crop rows off the bottom of a 32-bit PNG (headless Chromium pads the shot with its
window-chrome height, which we do not want in the README images)."""

import struct
import sys
import zlib


def read_png(path):
    data = open(path, 'rb').read()
    assert data[:8] == b'\x89PNG\r\n\x1a\x1a'[:8] or data[:8] == b'\x89PNG\r\n\x1a\n'
    pos, idat, header = 8, bytearray(), None
    while pos < len(data):
        length = struct.unpack('>I', data[pos:pos + 4])[0]
        tag = data[pos + 4:pos + 8]
        payload = data[pos + 8:pos + 8 + length]
        if tag == b'IHDR':
            header = struct.unpack('>IIBBBBB', payload)
        elif tag == b'IDAT':
            idat += payload
        pos += 12 + length
    width, height, depth, color, _, _, interlace = header
    assert depth == 8 and color in (2, 6) and interlace == 0, header
    return width, height, 4 if color == 6 else 3, zlib.decompress(bytes(idat))


def unfilter(width, height, channels, raw):
    stride = width * channels
    out = bytearray(stride * height)
    pos = 0
    for y in range(height):
        filter_type = raw[pos]
        pos += 1
        line = bytearray(raw[pos:pos + stride])
        pos += stride
        prior = out[(y - 1) * stride:y * stride] if y else bytearray(stride)
        for x in range(stride):
            a = line[x - channels] if x >= channels else 0
            b = prior[x]
            c = prior[x - channels] if x >= channels else 0
            if filter_type == 1:
                line[x] = (line[x] + a) & 0xFF
            elif filter_type == 2:
                line[x] = (line[x] + b) & 0xFF
            elif filter_type == 3:
                line[x] = (line[x] + ((a + b) >> 1)) & 0xFF
            elif filter_type == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pred = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[x] = (line[x] + pred) & 0xFF
        out[y * stride:(y + 1) * stride] = line
    return out


def write_png(path, width, height, channels, pixels):
    stride = width * channels
    raw = bytearray()
    for y in range(height):
        raw.append(2 if y else 0)  # up filter compresses these gradients well
        line = pixels[y * stride:(y + 1) * stride]
        if y:
            prior = pixels[(y - 1) * stride:y * stride]
            raw += bytes((line[i] - prior[i]) & 0xFF for i in range(stride))
        else:
            raw += line

    def chunk(tag, payload):
        return (struct.pack('>I', len(payload)) + tag + payload
                + struct.pack('>I', zlib.crc32(tag + payload) & 0xFFFFFFFF))

    with open(path, 'wb') as handle:
        handle.write(b'\x89PNG\r\n\x1a\n'
                     + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6 if channels == 4 else 2, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(bytes(raw), 9))
                     + chunk(b'IEND', b''))


if __name__ == '__main__':
    source, target, keep = sys.argv[1], sys.argv[2], int(sys.argv[3])
    width, height, channels, raw = read_png(source)
    pixels = unfilter(width, height, channels, raw)
    keep = min(keep, height)
    write_png(target, width, keep, channels, pixels[:width * channels * keep])
    print(f'{target}: {width}x{keep}')
