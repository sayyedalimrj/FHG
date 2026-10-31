#!/usr/bin/env python3
"""Generate FHG branding icon in standard multi-resolution PNG-in-ICO format.
No external packages are needed. Artwork is original and generated at build time.
"""
import math
import struct
import sys
import zlib
from pathlib import Path

def png_chunk(tag, data):
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff)

def polygon(x, y, vertices):
    odd = False
    j = len(vertices) - 1
    for i in range(len(vertices)):
        xi, yi = vertices[i]
        xj, yj = vertices[j]
        if ((yi > y) != (yj > y)) and x < (xj - xi) * (y - yi) / (yj - yi + 1e-10) + xi:
            odd = not odd
        j = i
    return odd

def artwork(n):
    shield = [(50, 13), (81, 26), (79, 62), (67, 80), (50, 90),
              (33, 80), (21, 62), (19, 26)]
    inner = [(50, 20), (74, 30), (72, 60), (62, 74), (50, 82),
             (38, 74), (28, 60), (26, 30)]
    # A stylized protected proxy route, with a white center and teal nodes.
    route = [(35, 49), (45, 49), (49, 42), (59, 42)]
    raw = bytearray()
    for row in range(n):
        raw.append(0)
        for col in range(n):
            x = (col + 0.5) * 100 / n
            y = (row + 0.5) * 100 / n
            radius = max(abs(x-50)-43, abs(y-50)-43)
            if radius > 0:
                alpha = 0
                r, g, b = 0, 0, 0
            else:
                alpha = 255
                r, g, b = 13, int(25 + 0.12*y), 46
                if polygon(x,y,shield):
                    r,g,b = 18,188,167
                if polygon(x,y,inner):
                    r,g,b = 17,55,80
                # central white lock/proxy port
                if 39 < x < 61 and 50 < y < 70:
                    r,g,b = 236,251,249
                if 44 < x < 56 and 53 < y < 61:
                    r,g,b = 17,55,80
                if 43 < x < 57 and 41 < y < 57 and math.hypot((x-50)*1.0,(y-48)*1.2)>7:
                    r,g,b = 236,251,249
                if 35 < x < 42 and abs(y-58)<3:
                    r,g,b = 18,188,167
                if 59 < x < 66 and abs(y-58)<3:
                    r,g,b = 18,188,167
                if (x-34)**2+(y-58)**2<15 or (x-66)**2+(y-58)**2<15:
                    r,g,b = 242,193,71
            raw.extend((r,g,b,alpha))
    def chunk(tag, b):
        return png_chunk(tag,b)
    return (b"\x89PNG\r\n\x1a\n" +
            chunk(b"IHDR", struct.pack(">IIBBBBB", n,n,8,6,0,0,0)) +
            chunk(b"IDAT", zlib.compress(bytes(raw),9)) +
            chunk(b"IEND",b""))

def main(output):
    sizes=[16,24,32,48,64,128,256]
    data=[artwork(n) for n in sizes]
    ico=bytearray(struct.pack("<HHH",0,1,len(sizes)))
    offset=6+16*len(sizes)
    for n,png in zip(sizes,data):
        ico.extend(struct.pack("<BBBBHHII",n if n < 256 else 0,n if n < 256 else 0,
                               0,0,1,32,len(png),offset))
        offset+=len(png)
    for blob in data: ico.extend(blob)
    target=Path(output)
    target.parent.mkdir(parents=True,exist_ok=True)
    target.write_bytes(ico)
    print(f"Generated {target}: {len(sizes)} icon sizes, {len(ico)} bytes")

if __name__=="__main__":
    main(sys.argv[1] if len(sys.argv)>1 else "native/app/resources/FHG.ico")
