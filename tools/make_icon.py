"""Create a Windows ICO from the locally extracted Golden Fishing Rod sprite."""
from pathlib import Path
from PIL import Image
import io
import struct

root = Path(__file__).resolve().parents[1]
sprite = Image.open(root / "assets" / "golden-fishing-rod.png").convert("RGBA")
sprite = sprite.crop(sprite.getbbox())
sizes = (16, 20, 24, 32, 40, 48, 64, 128, 256)
frames = []
for size in sizes:
    padding = max(1, round(size / 16))
    scale = min((size - padding * 2) / sprite.width, (size - padding * 2) / sprite.height)
    rod = sprite.resize((max(1, round(sprite.width * scale)), max(1, round(sprite.height * scale))), Image.Resampling.NEAREST)
    frame = Image.new("RGBA", (size, size))
    frame.alpha_composite(rod, ((size - rod.width) // 2, (size - rod.height) // 2))
    output = io.BytesIO()
    frame.save(output, format="PNG")
    frames.append((size, output.getvalue()))
    if size == 256:
        frame.save(root / "assets" / "golden-fishing-rod-preview.png")

# Explicit frames preserve the pixel-art scaling at every Windows icon size.
offset = 6 + 16 * len(frames)
directory = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
for size, data in frames:
    directory.extend(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
    offset += len(data)
(root / "assets" / "golden-fishing-rod.ico").write_bytes(directory + b"".join(data for _, data in frames))
print("Created Golden Fishing Rod icon: " + ", ".join(map(str, sizes)))
