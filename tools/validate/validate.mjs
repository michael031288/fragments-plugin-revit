import fs from "node:fs";
import pako from "pako";

const path = process.argv[2];
if (!path) {
  console.error("Usage: node validate.mjs <file.frag>");
  process.exit(1);
}

const bytes = new Uint8Array(fs.readFileSync(path));
const zlib = bytes.length >= 2 && bytes[0] === 0x78 && [0x01, 0x9c, 0xda].includes(bytes[1]);
const raw = zlib ? pako.inflate(bytes) : bytes;
const identifier = String.fromCharCode(raw[4], raw[5], raw[6], raw[7]);

if (identifier !== "0001") {
  console.error(`Unexpected file identifier '${identifier}', expected 0001.`);
  process.exit(2);
}

console.log(
  JSON.stringify(
    {
      path,
      compressed: zlib,
      compressedBytes: bytes.length,
      rawBytes: raw.length,
      identifier,
    },
    null,
    2,
  ),
);
