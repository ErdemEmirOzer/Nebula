#!/usr/bin/env node

/*
 * Nebula's authored GLBs use the Blender-exported custom `_AALPHA` vertex
 * attribute for clouds and rings. glTFast intentionally ignores unknown
 * attributes, so those layers arrived in Unity as fully opaque white meshes.
 *
 * This deterministic import-normalization pass copies `_AALPHA` into the
 * standard glTF COLOR_0 alpha channel (RGB stays neutral white). It changes no
 * positions, topology, UVs, normals or authored material values. Run it only
 * on the project copies; the delivered source archive remains untouched.
 */

import fs from "node:fs";
import path from "node:path";

const JSON_CHUNK = 0x4e4f534a;
const BIN_CHUNK = 0x004e4942;
const GLB_MAGIC = 0x46546c67;

function align4(value) {
  return (value + 3) & ~3;
}

function parseGlb(buffer) {
  if (buffer.readUInt32LE(0) !== GLB_MAGIC || buffer.readUInt32LE(4) !== 2) {
    throw new Error("Only glTF 2.0 binary files are supported.");
  }

  let cursor = 12;
  let json = null;
  let binary = Buffer.alloc(0);
  while (cursor < buffer.length) {
    const length = buffer.readUInt32LE(cursor);
    const type = buffer.readUInt32LE(cursor + 4);
    const payload = buffer.subarray(cursor + 8, cursor + 8 + length);
    if (type === JSON_CHUNK) {
      json = JSON.parse(payload.toString("utf8").trimEnd());
    } else if (type === BIN_CHUNK) {
      binary = Buffer.from(payload);
    }
    cursor += 8 + length;
  }

  if (!json) throw new Error("GLB has no JSON chunk.");
  return { json, binary };
}

function readScalarFloatAccessor(document, binary, accessorIndex) {
  const accessor = document.accessors[accessorIndex];
  if (!accessor || accessor.type !== "SCALAR" || accessor.componentType !== 5126) {
    throw new Error(`_AALPHA accessor ${accessorIndex} must be FLOAT/SCALAR.`);
  }

  const view = document.bufferViews[accessor.bufferView];
  if (!view || (view.buffer ?? 0) !== 0) {
    throw new Error(`_AALPHA accessor ${accessorIndex} must use GLB buffer 0.`);
  }

  const base = (view.byteOffset ?? 0) + (accessor.byteOffset ?? 0);
  const stride = view.byteStride ?? 4;
  const values = new Float32Array(accessor.count);
  for (let index = 0; index < accessor.count; index += 1) {
    values[index] = binary.readFloatLE(base + index * stride);
  }
  return values;
}

function readFloatColourAccessor(document, binary, accessorIndex, expectedCount) {
  if (accessorIndex === undefined) return null;
  const accessor = document.accessors[accessorIndex];
  if (!accessor || accessor.componentType !== 5126 ||
      (accessor.type !== "VEC3" && accessor.type !== "VEC4") ||
      accessor.count !== expectedCount) {
    throw new Error(`COLOR_0 accessor ${accessorIndex} must be FLOAT/VEC3-or-VEC4 and match _AALPHA.`);
  }

  const componentCount = accessor.type === "VEC3" ? 3 : 4;
  const view = document.bufferViews[accessor.bufferView];
  if (!view || (view.buffer ?? 0) !== 0) {
    throw new Error(`COLOR_0 accessor ${accessorIndex} must use GLB buffer 0.`);
  }

  const base = (view.byteOffset ?? 0) + (accessor.byteOffset ?? 0);
  const stride = view.byteStride ?? componentCount * 4;
  const values = new Float32Array(accessor.count * componentCount);
  for (let index = 0; index < accessor.count; index += 1) {
    for (let component = 0; component < componentCount; component += 1) {
      values[index * componentCount + component] =
        binary.readFloatLE(base + index * stride + component * 4);
    }
  }
  return { componentCount, values };
}

function normalize(document, originalBinary) {
  let binary = Buffer.from(originalBinary);
  let converted = 0;

  for (const mesh of document.meshes ?? []) {
    for (const primitive of mesh.primitives ?? []) {
      const attributes = primitive.attributes ?? {};
      if (attributes._AALPHA === undefined) continue;

      const alphas = readScalarFloatAccessor(document, binary, attributes._AALPHA);
      const existingColours =
        readFloatColourAccessor(document, binary, attributes.COLOR_0, alphas.length);
      const byteOffset = align4(binary.length);
      const padding = Buffer.alloc(byteOffset - binary.length);
      const colors = Buffer.alloc(alphas.length * 16);
      let minimumAlpha = Number.POSITIVE_INFINITY;
      let maximumAlpha = Number.NEGATIVE_INFINITY;

      for (let index = 0; index < alphas.length; index += 1) {
        const alpha = Math.min(1, Math.max(0, alphas[index]));
        const offset = index * 16;
        const colourOffset = existingColours == null
          ? -1
          : index * existingColours.componentCount;
        colors.writeFloatLE(existingColours == null ? 1 : existingColours.values[colourOffset], offset);
        colors.writeFloatLE(existingColours == null ? 1 : existingColours.values[colourOffset + 1], offset + 4);
        colors.writeFloatLE(existingColours == null ? 1 : existingColours.values[colourOffset + 2], offset + 8);
        colors.writeFloatLE(alpha, offset + 12);
        minimumAlpha = Math.min(minimumAlpha, alpha);
        maximumAlpha = Math.max(maximumAlpha, alpha);
      }

      binary = Buffer.concat([binary, padding, colors]);
      const bufferViewIndex = document.bufferViews.length;
      document.bufferViews.push({
        buffer: 0,
        byteOffset,
        byteLength: colors.length,
        target: 34962,
      });
      const accessorIndex = document.accessors.length;
      document.accessors.push({
        bufferView: bufferViewIndex,
        byteOffset: 0,
        componentType: 5126,
        count: alphas.length,
        type: "VEC4",
        min: [1, 1, 1, minimumAlpha],
        max: [1, 1, 1, maximumAlpha],
      });
      attributes.COLOR_0 = accessorIndex;
      delete attributes._AALPHA;
      converted += 1;
    }
  }

  if (converted > 0) document.buffers[0].byteLength = binary.length;
  return { binary, converted };
}

function encodeGlb(document, binary) {
  const jsonSource = Buffer.from(JSON.stringify(document), "utf8");
  const jsonLength = align4(jsonSource.length);
  const json = Buffer.alloc(jsonLength, 0x20);
  jsonSource.copy(json);

  const binaryLength = align4(binary.length);
  const paddedBinary = Buffer.alloc(binaryLength);
  binary.copy(paddedBinary);

  const totalLength = 12 + 8 + json.length + 8 + paddedBinary.length;
  const result = Buffer.alloc(totalLength);
  result.writeUInt32LE(GLB_MAGIC, 0);
  result.writeUInt32LE(2, 4);
  result.writeUInt32LE(totalLength, 8);
  result.writeUInt32LE(json.length, 12);
  result.writeUInt32LE(JSON_CHUNK, 16);
  json.copy(result, 20);
  const binaryHeader = 20 + json.length;
  result.writeUInt32LE(paddedBinary.length, binaryHeader);
  result.writeUInt32LE(BIN_CHUNK, binaryHeader + 4);
  paddedBinary.copy(result, binaryHeader + 8);
  return result;
}

function collectGlbs(root) {
  const results = [];
  for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
    const absolute = path.join(root, entry.name);
    if (entry.isDirectory()) results.push(...collectGlbs(absolute));
    else if (entry.isFile() && entry.name.toLowerCase().endsWith(".glb")) results.push(absolute);
  }
  return results;
}

const requestedRoot = process.argv[2];
if (!requestedRoot) {
  throw new Error("Usage: normalize-gltf-vertex-alpha.mjs <GLB directory>");
}

let changedFiles = 0;
let changedPrimitives = 0;
for (const file of collectGlbs(path.resolve(requestedRoot)).sort()) {
  const source = fs.readFileSync(file);
  const parsed = parseGlb(source);
  const normalized = normalize(parsed.json, parsed.binary);
  if (normalized.converted === 0) continue;

  fs.writeFileSync(file, encodeGlb(parsed.json, normalized.binary));
  changedFiles += 1;
  changedPrimitives += normalized.converted;
  process.stdout.write(`${path.relative(process.cwd(), file)}: ${normalized.converted}\n`);
}

process.stdout.write(`Normalized ${changedPrimitives} primitives in ${changedFiles} GLB files.\n`);
