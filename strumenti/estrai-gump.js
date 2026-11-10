// Estrae le gump di Ultima Online (Gumpart.mul / Gumpidx.mul) in PNG.
// Uso: node estrai-gump.js <cartella UO> <cartella uscita> [elenco|id,id,...|grandi]
const fs = require('fs'), zlib = require('zlib'), path = require('path');
const [, , uo, out, cosa = 'grandi'] = process.argv;
const idx = fs.readFileSync(path.join(uo, 'Gumpidx.mul')), mul = fs.readFileSync(path.join(uo, 'Gumpart.mul'));
const n = idx.length / 12;
function info(id) {
	const o = idx.readInt32LE(id * 12), l = idx.readInt32LE(id * 12 + 4), e = idx.readInt32LE(id * 12 + 8);
	if (o < 0 || l <= 0) return null;
	return { o, l, w: (e >> 16) & 0xffff, h: e & 0xffff };
}
function rgba(id) {
	const g = info(id); if (!g || !g.w || !g.h) return null;
	const px = Buffer.alloc(g.w * g.h * 4);
	for (let y = 0; y < g.h; y++) {
		const start = g.o + mul.readInt32LE(g.o + y * 4) * 4;
		const end = y < g.h - 1 ? g.o + mul.readInt32LE(g.o + (y + 1) * 4) * 4 : g.o + g.l;
		let x = 0;
		for (let p = start; p < end && x < g.w; p += 4) {
			const c = mul.readUInt16LE(p), run = mul.readUInt16LE(p + 2);
			for (let k = 0; k < run && x < g.w; k++, x++) {
				if (!c) continue;
				const i = (y * g.w + x) * 4;
				px[i] = ((c >> 10) & 31) * 255 / 31; px[i + 1] = ((c >> 5) & 31) * 255 / 31; px[i + 2] = (c & 31) * 255 / 31; px[i + 3] = 255;
			}
		}
	}
	return { w: g.w, h: g.h, px };
}
function crc(b) { let c, t = []; for (let n = 0; n < 256; n++) { c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } c = 0xffffffff; for (const x of b) c = t[(c ^ x) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; }
function chunk(tipo, dati) { const l = Buffer.alloc(4); l.writeUInt32BE(dati.length); const td = Buffer.concat([Buffer.from(tipo), dati]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); }
function png(w, h, px) {
	const raw = Buffer.alloc((w * 4 + 1) * h);
	for (let y = 0; y < h; y++) { raw[y * (w * 4 + 1)] = 0; px.copy(raw, y * (w * 4 + 1) + 1, y * w * 4, (y + 1) * w * 4); }
	const ih = Buffer.alloc(13); ih.writeUInt32BE(w, 0); ih.writeUInt32BE(h, 4); ih[8] = 8; ih[9] = 6;
	return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ih), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}
if (process.argv[5]) { const parti = []; for (const id of process.argv[5].split(',').map(x => parseInt(x))) { const g = info(id); if (!g) { console.error('manca 0x' + id.toString(16)); process.exit(1); } const r = rgba(id); const t = Buffer.alloc(12 + r.w * r.h * 2); t.writeInt32LE(id, 0); t.writeInt32LE(r.w, 4); t.writeInt32LE(r.h, 8); for (let i = 0; i < r.w * r.h; i++) { const a = r.px[i * 4 + 3]; const c = a ? (Math.round(r.px[i * 4] * 31 / 255) << 10) | (Math.round(r.px[i * 4 + 1] * 31 / 255) << 5) | Math.round(r.px[i * 4 + 2] * 31 / 255) : 0; t.writeUInt16LE(c || (a ? 1 : 0), 12 + i * 2); } parti.push(t); } fs.writeFileSync(out, zlib.deflateRawSync(Buffer.concat(parti), { level: 9 })); console.log('scritto', out, fs.statSync(out).size); process.exit(0); }
fs.mkdirSync(out, { recursive: true });
let ids = [];
if (cosa === 'elenco' || cosa === 'grandi') { for (let i = 0; i < n; i++) { const g = info(i); if (g && g.w * g.h >= (cosa === 'grandi' ? 120000 : 1)) ids.push(i); } }
else ids = cosa.split(',').map(s => parseInt(s));
for (const id of ids) {
	const g = info(id); if (cosa === 'elenco') { console.log('0x' + id.toString(16), g.w + 'x' + g.h); continue; }
	const r = rgba(id); if (!r) continue;
	fs.writeFileSync(path.join(out, 'gump-0x' + id.toString(16).padStart(4, '0') + '.png'), png(r.w, r.h, r.px));
	console.log('0x' + id.toString(16), r.w + 'x' + r.h);
}

// Modalita' "risorsa": node estrai-gump.js <UO> <file .bin> risorsa id,id,...
// Scrive le gump per il launcher (Grafica.cs): per ognuna id, larghezza, altezza (int32) e i pixel 16 bit 1-5-5-5
// come nel gioco (0 = trasparente); tutto compresso deflate.
