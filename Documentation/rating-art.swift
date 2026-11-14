// Draws the faces of the result screen's "how was this chart?" prompt.
// Usage: swift rating-art.swift <output folder>
// Everything is drawn here from shapes; no existing artwork is read. The plates, the panel and the
// focus ring the prompt is made of are マルチ演奏's (Documentation/multi-art.swift).
import AppKit
import CoreGraphics
import Foundation

let out = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "out")
try? FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)

func rgb(_ hex: UInt32, _ a: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: CGFloat((hex >> 16) & 255) / 255, green: CGFloat((hex >> 8) & 255) / 255, blue: CGFloat(hex & 255) / 255, alpha: a)
}
let black = rgb(0x000000), white = rgb(0xffffff)

// A canvas with the origin at the top left, like the game's skin coordinates.
func draw(_ name: String, _ w: Int, _ h: Int, _ body: (CGContext) -> Void) {
    let space = CGColorSpace(name: CGColorSpace.sRGB)!
    let c = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: space,
                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    c.translateBy(x: 0, y: CGFloat(h)); c.scaleBy(x: 1, y: -1)
    c.setAllowsAntialiasing(true); c.interpolationQuality = .high
    body(c)
    let rep = NSBitmapImageRep(cgImage: c.makeImage()!)
    try! rep.representation(using: .png, properties: [:])!.write(to: out.appendingPathComponent(name))
}

func rr(_ r: CGRect, _ radius: CGFloat) -> CGPath { CGPath(roundedRect: r, cornerWidth: min(radius, r.width / 2), cornerHeight: min(radius, r.height / 2), transform: nil) }
func fill(_ c: CGContext, _ p: CGPath, _ color: CGColor) { c.addPath(p); c.setFillColor(color); c.fillPath() }
func stroke(_ c: CGContext, _ p: CGPath, _ color: CGColor, _ width: CGFloat) {
    c.addPath(p); c.setStrokeColor(color); c.setLineWidth(width); c.setLineJoin(.round); c.setLineCap(.round); c.strokePath()
}
func circle(_ x: CGFloat, _ y: CGFloat, _ r: CGFloat) -> CGPath { CGPath(ellipseIn: CGRect(x: x - r, y: y - r, width: r * 2, height: r * 2), transform: nil) }
func poly(_ pts: [(CGFloat, CGFloat)]) -> CGPath {
    let p = CGMutablePath(); p.move(to: CGPoint(x: pts[0].0, y: pts[0].1))
    for q in pts.dropFirst() { p.addLine(to: CGPoint(x: q.0, y: q.1)) }
    p.closeSubpath(); return p
}
func star(_ x: CGFloat, _ y: CGFloat, _ outer: CGFloat, _ inner: CGFloat, _ points: Int = 4, _ turn: CGFloat = -.pi / 2) -> CGPath {
    var pts: [(CGFloat, CGFloat)] = []
    for i in 0..<(points * 2) {
        let r = i % 2 == 0 ? outer : inner, a = turn + CGFloat(i) * .pi / CGFloat(points)
        pts.append((x + cos(a) * r, y + sin(a) * r))
    }
    return poly(pts)
}

let cream: UInt32 = 0xfff3d6, don: UInt32 = 0xff5a36
let cx: CGFloat = 76, cy: CGFloat = 84, r: CGFloat = 60

// The don drum seen from the front, as the stamps draw it: a rim, then the skin.
func head(_ c: CGContext) {
    fill(c, circle(cx, cy, r + 8), black)
    fill(c, circle(cx, cy, r + 1), rgb(cream))
    fill(c, circle(cx, cy, r - 9), black)
    fill(c, circle(cx, cy, r - 14), rgb(don))
    c.saveGState(); c.addPath(circle(cx, cy, r - 14)); c.clip()
    c.setFillColor(rgb(0xffffff, 0.28)); c.fillEllipse(in: CGRect(x: cx - 58, y: cy - 70, width: 100, height: 64))
    c.restoreGState()
}

// A fist with its thumb out, up or down, about (x, y).
func thumb(_ c: CGContext, _ x: CGFloat, _ y: CGFloat, up: Bool) {
    let k: CGFloat = up ? 1 : -1
    let fist = rr(CGRect(x: x - 19, y: y - 15, width: 38, height: 30), 13)
    let finger = rr(CGRect(x: x - 16, y: up ? y - 37 : y + 7, width: 17, height: 30), 8.5)
    stroke(c, fist, black, 10); stroke(c, finger, black, 10)
    fill(c, fist, rgb(cream)); fill(c, finger, rgb(cream))
    // The folded fingers.
    let folds = CGMutablePath()
    for i in 0..<2 {
        let fy = y + k * (CGFloat(i) * 9 - 2)
        folds.move(to: CGPoint(x: x + 4, y: fy)); folds.addLine(to: CGPoint(x: x + 15, y: fy))
    }
    stroke(c, folds, rgb(0x000000, 0.55), 3)
}

// An open hand, held up: "not now".
func palm(_ c: CGContext, _ x: CGFloat, _ y: CGFloat) {
    let hand = rr(CGRect(x: x - 18, y: y - 12, width: 36, height: 32), 14)
    stroke(c, hand, black, 10)
    var fingers: [CGPath] = []
    for i in 0..<4 { fingers.append(rr(CGRect(x: x - 18 + CGFloat(i) * 9.4, y: y - 30 + abs(CGFloat(i) - 1.5) * 3, width: 8.6, height: 26), 4.3)) }
    for f in fingers { stroke(c, f, black, 9) }
    fill(c, hand, rgb(cream))
    for f in fingers { fill(c, f, rgb(cream)) }
    let lines = CGMutablePath()
    for i in 1..<4 { let lx = x - 18 + CGFloat(i) * 9.4 - 0.4; lines.move(to: CGPoint(x: lx, y: y - 22)); lines.addLine(to: CGPoint(x: lx, y: y - 10)) }
    stroke(c, lines, rgb(0x000000, 0.55), 2.5)
}

let ex: CGFloat = 19, ey = cy - 8, my = cy + 20

// 好评: laughing eyes, a wide grin, a blush, a thumb up and sparkles.
draw("good.png", 160, 160) { c in
    head(c)
    fill(c, circle(cx - 31, cy + 12, 9), rgb(0xff6b8f, 0.8)); fill(c, circle(cx + 31, cy + 12, 9), rgb(0xff6b8f, 0.8))
    for x in [cx - ex, cx + ex] {
        let p = CGMutablePath(); p.addArc(center: CGPoint(x: x, y: ey + 4), radius: 8, startAngle: .pi, endAngle: 0, clockwise: false)
        stroke(c, p, black, 5.5)
    }
    let p = CGMutablePath(); p.move(to: CGPoint(x: cx - 18, y: my - 6)); p.addLine(to: CGPoint(x: cx + 18, y: my - 6))
    p.addQuadCurve(to: CGPoint(x: cx - 18, y: my - 6), control: CGPoint(x: cx, y: my + 26)); p.closeSubpath()
    stroke(c, p, black, 5); fill(c, p, rgb(0xb3202a))
    c.saveGState(); c.addPath(p); c.clip(); c.setFillColor(white); c.fill(CGRect(x: cx - 18, y: my - 6, width: 36, height: 6)); c.restoreGState()
    for (x, y, s) in [(134.0, 28.0, 15.0), (20.0, 30.0, 10.0)] {
        fill(c, star(CGFloat(x), CGFloat(y), CGFloat(s) + 4, CGFloat(s) * 0.3 + 3), black)
        fill(c, star(CGFloat(x), CGFloat(y), CGFloat(s), CGFloat(s) * 0.28), rgb(0xfff27a))
    }
    thumb(c, 130, 122, up: true)
}

// 取消: looking aside, a flat mouth, a drop of sweat and a hand held up.
draw("skip.png", 160, 160) { c in
    head(c)
    for x in [cx - ex, cx + ex] {
        fill(c, circle(x, ey, 9.5), black); fill(c, circle(x, ey, 6.5), white); fill(c, circle(x - 2.5, ey + 1, 3.6), black)
    }
    let p = CGMutablePath(); p.move(to: CGPoint(x: cx - 12, y: my + 1)); p.addLine(to: CGPoint(x: cx + 12, y: my + 1)); stroke(c, p, black, 5.5)
    let drop = CGMutablePath(); drop.move(to: CGPoint(x: 22, y: 30))
    drop.addQuadCurve(to: CGPoint(x: 22, y: 60), control: CGPoint(x: 3, y: 56)); drop.addQuadCurve(to: CGPoint(x: 22, y: 30), control: CGPoint(x: 41, y: 56))
    stroke(c, drop, black, 5); fill(c, drop, rgb(0x6fd3ff))
    palm(c, 131, 118)
}

// 差评: lowered brows, a frown, a tear, a thumb down and a scribble of gloom.
draw("bad.png", 160, 160) { c in
    head(c)
    for (x, left) in [(cx - ex, true), (cx + ex, false)] {
        fill(c, circle(x, ey + 3, 6.5), black)
        let brow = CGMutablePath()
        brow.move(to: CGPoint(x: x + (left ? -11 : 11), y: ey - 6)); brow.addLine(to: CGPoint(x: x + (left ? 9 : -9), y: ey - 13))
        stroke(c, brow, black, 5.5)
    }
    let p = CGMutablePath(); p.addArc(center: CGPoint(x: cx, y: my + 15), radius: 15, startAngle: .pi * 1.15, endAngle: .pi * 1.85, clockwise: false); stroke(c, p, black, 5.5)
    let tear = CGMutablePath(); tear.move(to: CGPoint(x: cx - ex - 9, y: ey + 12))
    tear.addQuadCurve(to: CGPoint(x: cx - ex - 9, y: ey + 32), control: CGPoint(x: cx - ex - 21, y: ey + 30))
    tear.addQuadCurve(to: CGPoint(x: cx - ex - 9, y: ey + 12), control: CGPoint(x: cx - ex + 3, y: ey + 30))
    stroke(c, tear, black, 4.5); fill(c, tear, rgb(0x6fd3ff))
    let gloom = CGMutablePath()
    for i in 0..<3 { let gx = 118 + CGFloat(i) * 13; gloom.move(to: CGPoint(x: gx, y: 14)); gloom.addLine(to: CGPoint(x: gx, y: 34 + CGFloat(i % 2) * 8)) }
    stroke(c, gloom, black, 10); stroke(c, gloom, rgb(0xa884f0), 5)
    thumb(c, 130, 116, up: false)
}
print("done")
