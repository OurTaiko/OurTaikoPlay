// Draws OurTaikoPlay's ゴーストバトル art. Usage: swift ghost-art.swift <output folder>
// Everything is drawn here from shapes; no existing artwork is read.
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
func gradient(_ c: CGContext, _ p: CGPath, _ colors: [CGColor], _ from: CGPoint, _ to: CGPoint) {
    c.saveGState(); c.addPath(p); c.clip()
    let g = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB)!, colors: colors as CFArray, locations: nil)!
    c.drawLinearGradient(g, start: from, end: to, options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
    c.restoreGState()
}
func radial(_ c: CGContext, _ center: CGPoint, _ radius: CGFloat, _ colors: [CGColor]) {
    let g = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB)!, colors: colors as CFArray, locations: nil)!
    c.drawRadialGradient(g, startCenter: center, startRadius: 0, endCenter: center, endRadius: radius, options: [])
}
func circle(_ x: CGFloat, _ y: CGFloat, _ r: CGFloat) -> CGPath { CGPath(ellipseIn: CGRect(x: x - r, y: y - r, width: r * 2, height: r * 2), transform: nil) }
func poly(_ pts: [(CGFloat, CGFloat)]) -> CGPath {
    let p = CGMutablePath(); p.move(to: CGPoint(x: pts[0].0, y: pts[0].1))
    for q in pts.dropFirst() { p.addLine(to: CGPoint(x: q.0, y: q.1)) }
    p.closeSubpath(); return p
}
func star(_ x: CGFloat, _ y: CGFloat, _ outer: CGFloat, _ inner: CGFloat, _ points: Int = 5, _ turn: CGFloat = -.pi / 2) -> CGPath {
    var pts: [(CGFloat, CGFloat)] = []
    for i in 0..<(points * 2) {
        let r = i % 2 == 0 ? outer : inner, a = turn + CGFloat(i) * .pi / CGFloat(points)
        pts.append((x + cos(a) * r, y + sin(a) * r))
    }
    return poly(pts)
}

// The skin's plate look: a black outline, a flat colour, a light edge on top and a dark one below.
func plate(_ c: CGContext, _ r: CGRect, radius: CGFloat, color: UInt32, outline: CGFloat = 6, shadow: Bool = true) {
    if shadow {
        c.saveGState(); c.setShadow(offset: CGSize(width: 0, height: -6), blur: 8, color: rgb(0, 0.45))
        fill(c, rr(r, radius), black); c.restoreGState()
    }
    fill(c, rr(r, radius), black)
    let inner = r.insetBy(dx: outline, dy: outline)
    fill(c, rr(inner, radius - outline), rgb(color))
    c.saveGState(); c.addPath(rr(inner, radius - outline)); c.clip()
    c.setFillColor(rgb(0xffffff, 0.55)); c.fill(CGRect(x: inner.minX, y: inner.minY, width: inner.width, height: 4))
    c.setFillColor(rgb(0x000000, 0.28)); c.fill(CGRect(x: inner.minX, y: inner.maxY - 5, width: inner.width, height: 5))
    c.restoreGState()
}


// A ghost: a round head on a body with a wavy hem, the skin's outline, two eyes and a mouth.
func ghost(_ c: CGContext, _ x: CGFloat, _ y: CGFloat, _ s: CGFloat, body: UInt32 = 0xffffff, tongue: Bool = false, wink: Bool = false) {
    let p = CGMutablePath()
    p.move(to: CGPoint(x: x - 40 * s, y: y + 44 * s))
    p.addLine(to: CGPoint(x: x - 40 * s, y: y - 6 * s))
    p.addArc(center: CGPoint(x: x, y: y - 6 * s), radius: 40 * s, startAngle: .pi, endAngle: 0, clockwise: false)
    p.addLine(to: CGPoint(x: x + 40 * s, y: y + 44 * s))
    // The hem: three scallops.
    for i in 0..<3 {
        let right = x + 40 * s - CGFloat(i) * 80 * s / 3, left = right - 80 * s / 3
        p.addQuadCurve(to: CGPoint(x: left, y: y + 44 * s), control: CGPoint(x: (left + right) / 2, y: y + 22 * s))
    }
    p.closeSubpath()
    stroke(c, p, black, 12 * s); fill(c, p, rgb(body))
    c.saveGState(); c.addPath(p); c.clip()
    c.setFillColor(rgb(0x9fc6ff, 0.45)); c.fillEllipse(in: CGRect(x: x - 6 * s, y: y + 4 * s, width: 70 * s, height: 60 * s))
    c.restoreGState()
    fill(c, CGPath(ellipseIn: CGRect(x: x - 24 * s, y: y - 20 * s, width: 14 * s, height: 20 * s), transform: nil), black)
    if wink {
        let w = CGMutablePath(); w.move(to: CGPoint(x: x + 8 * s, y: y - 14 * s)); w.addLine(to: CGPoint(x: x + 22 * s, y: y - 9 * s)); w.addLine(to: CGPoint(x: x + 8 * s, y: y - 4 * s))
        stroke(c, w, black, 5 * s)
    } else {
        fill(c, CGPath(ellipseIn: CGRect(x: x + 10 * s, y: y - 20 * s, width: 14 * s, height: 20 * s), transform: nil), black)
    }
    let mouth = CGPath(ellipseIn: CGRect(x: x - 9 * s, y: y + 6 * s, width: 18 * s, height: 16 * s), transform: nil)
    fill(c, mouth, black)
    if tongue { fill(c, CGPath(ellipseIn: CGRect(x: x - 5 * s, y: y + 13 * s, width: 10 * s, height: 9 * s), transform: nil), rgb(0xff6b8f)) }
    fill(c, circle(x - 30 * s, y + 6 * s, 6 * s), rgb(0xff8fb0, 0.7)); fill(c, circle(x + 30 * s, y + 6 * s, 6 * s), rgb(0xff8fb0, 0.7))
}

// A will-o'-the-wisp: a flame with a round belly and a tail curling up.
func wisp(_ c: CGContext, _ x: CGFloat, _ y: CGFloat, _ s: CGFloat, outer: UInt32 = 0x4fc3ff, inner: UInt32 = 0xe8fbff, outline: Bool = true) {
    let p = CGMutablePath()
    p.move(to: CGPoint(x: x + 6 * s, y: y - 30 * s))
    p.addCurve(to: CGPoint(x: x + 20 * s, y: y + 8 * s), control1: CGPoint(x: x - 6 * s, y: y - 14 * s), control2: CGPoint(x: x + 24 * s, y: y - 8 * s))
    p.addArc(center: CGPoint(x: x, y: y + 8 * s), radius: 20 * s, startAngle: 0, endAngle: .pi, clockwise: false)
    p.addCurve(to: CGPoint(x: x + 6 * s, y: y - 30 * s), control1: CGPoint(x: x - 22 * s, y: y - 12 * s), control2: CGPoint(x: x - 2 * s, y: y - 12 * s))
    p.closeSubpath()
    if outline { stroke(c, p, black, 9 * s) }
    gradient(c, p, [rgb(inner), rgb(outer)], CGPoint(x: x, y: y + 20 * s), CGPoint(x: x, y: y - 30 * s))
    fill(c, circle(x, y + 10 * s, 9 * s), rgb(0xffffff, 0.85))
}

// ---------------------------------------------------------------- list background

draw("ghost_bg.png", 1920, 1080) { c in
    let all = CGRect(x: 0, y: 0, width: 1920, height: 1080)
    gradient(c, CGPath(rect: all, transform: nil), [rgb(0x071330), rgb(0x10305a), rgb(0x1b5566)], CGPoint(x: 0, y: 0), CGPoint(x: 0, y: 1080))
    var seed: UInt64 = 20261115
    func next() -> CGFloat { seed = seed &* 6364136223846793005 &+ 1442695040888963407; return CGFloat((seed >> 33) % 10000) / 10000 }
    // Stars.
    for _ in 0..<140 {
        let x = next() * 1920, y = next() * 640, r = 0.8 + next() * 2.2
        fill(c, circle(x, y, r), rgb(0xffffff, 0.25 + next() * 0.5))
    }
    // The moon, with its glow.
    radial(c, CGPoint(x: 1560, y: 230), 420, [rgb(0xbfe6ff, 0.28), rgb(0xbfe6ff, 0)])
    fill(c, circle(1560, 230, 132), rgb(0xf3fbff))
    for (dx, dy, r) in [(-44.0, -30.0, 26.0), (36.0, 22.0, 34.0), (-10.0, 58.0, 18.0), (58.0, -52.0, 14.0)] {
        fill(c, circle(1560 + CGFloat(dx), 230 + CGFloat(dy), CGFloat(r)), rgb(0xcfe6f5, 0.75))
    }
    // Mist in bands.
    for i in 0..<5 {
        let y = 520 + CGFloat(i) * 70, w = 900 + next() * 700, x = next() * 1400 - 200
        let band = CGPath(ellipseIn: CGRect(x: x, y: y, width: w, height: 90 + next() * 60), transform: nil)
        fill(c, band, rgb(0xcfeaff, 0.045))
    }
    // Waves of overlapping arcs along the lower third, as in the room screen.
    for row in 0..<7 {
        let y = 760 + CGFloat(row) * 54, shift: CGFloat = row % 2 == 0 ? 0 : 60
        for i in -1..<18 {
            let x = CGFloat(i) * 120 + shift
            for (k, radius) in [58, 40, 22].enumerated() {
                stroke(c, CGPath(ellipseIn: CGRect(x: x - CGFloat(radius), y: y - CGFloat(radius), width: CGFloat(radius) * 2, height: CGFloat(radius) * 2), transform: nil),
                       rgb(0xbfe6ff, 0.03 + 0.012 * CGFloat(k)), 5)
            }
        }
    }
    // Wisps adrift, far ones without an outline.
    for _ in 0..<26 {
        let x = next() * 1920, y = 120 + next() * 620, s = 0.5 + next() * 0.9
        radial(c, CGPoint(x: x, y: y), 70 * s, [rgb(0x4fc3ff, 0.22), rgb(0x4fc3ff, 0)])
        c.saveGState(); c.setAlpha(0.35 + next() * 0.3)
        wisp(c, x, y, s, outline: false)
        c.restoreGState()
    }
    // A string of pale lanterns across the top.
    let rope = CGMutablePath(); rope.move(to: CGPoint(x: -20, y: 60))
    rope.addQuadCurve(to: CGPoint(x: 1940, y: 60), control: CGPoint(x: 960, y: 190))
    stroke(c, rope, rgb(0x000000, 0.55), 5)
    for i in 0..<15 {
        let t = (CGFloat(i) + 0.5) / 15, x = -20 + 1960 * t
        let y = (1 - t) * (1 - t) * 60 + 2 * (1 - t) * t * 190 + t * t * 60
        let col: UInt32 = i % 2 == 0 ? 0x6fd3ff : 0xf3fbff
        radial(c, CGPoint(x: x, y: y + 34), 80, [rgb(col, 0.3), rgb(col, 0)])
        let body = CGRect(x: x - 22, y: y + 8, width: 44, height: 54)
        fill(c, rr(body, 20), black)
        fill(c, rr(body.insetBy(dx: 4, dy: 4), 16), rgb(col))
        c.setFillColor(rgb(0xffffff, 0.4)); c.fill(CGRect(x: x - 12, y: y + 16, width: 6, height: 30))
        fill(c, rr(CGRect(x: x - 12, y: y + 2, width: 24, height: 9), 3), black)
        fill(c, rr(CGRect(x: x - 12, y: y + 59, width: 24, height: 9), 3), black)
    }
    gradient(c, CGPath(rect: all, transform: nil), [rgb(0, 0), rgb(0, 0.38)], CGPoint(x: 0, y: 700), CGPoint(x: 0, y: 1080))
}

// ---------------------------------------------------------------- icons

// The mode's mark: a ghost with a wisp beside it.
draw("ghost_mark.png", 128, 128) { c in
    wisp(c, 100, 44, 0.8)
    ghost(c, 56, 70, 0.92, tongue: true)
}

// The recorded player's face where a play has no picture: a ghost on a night-blue disc.
draw("ghost_face.png", 128, 128) { c in
    fill(c, circle(64, 64, 62), black)
    fill(c, circle(64, 64, 55), rgb(0x2a4a8a))
    c.saveGState(); c.addPath(circle(64, 64, 55)); c.clip()
    c.setFillColor(rgb(0, 0.18)); c.fillEllipse(in: CGRect(x: -6, y: 76, width: 140, height: 90))
    ghost(c, 64, 66, 0.86)
    c.restoreGState()
}

draw("wisp.png", 72, 88) { c in wisp(c, 36, 48, 1.15) }

// VS, in the skin's outlined look: a red V and a blue S.
draw("vs.png", 240, 150) { c in
    let v = CGMutablePath(); v.move(to: CGPoint(x: 26, y: 30)); v.addLine(to: CGPoint(x: 66, y: 118)); v.addLine(to: CGPoint(x: 106, y: 30))
    let s = CGMutablePath(); s.move(to: CGPoint(x: 210, y: 46))
    s.addCurve(to: CGPoint(x: 170, y: 74), control1: CGPoint(x: 180, y: 14), control2: CGPoint(x: 128, y: 50))
    s.addCurve(to: CGPoint(x: 140, y: 104), control1: CGPoint(x: 232, y: 96), control2: CGPoint(x: 176, y: 140))
    for (p, col, light) in [(v, UInt32(0xff5a36), UInt32(0xffb199)), (s, UInt32(0x35a8ff), UInt32(0xa5dcff))] {
        c.saveGState(); c.setShadow(offset: CGSize(width: 0, height: -5), blur: 6, color: rgb(0, 0.45))
        stroke(c, p, black, 40); c.restoreGState()
        stroke(c, p, rgb(col), 24)
        c.saveGState(); c.translateBy(x: -3, y: -4); stroke(c, p, rgb(light, 0.9), 6); c.restoreGState()
    }
}

// ---------------------------------------------------------------- Entry's mode board (1160x460, as the skin's)

func board(_ name: String, open: Bool) {
    draw(name, 1160, 460) { c in
        let r = open ? CGRect(x: 110, y: 55, width: 940, height: 350) : CGRect(x: 110, y: 162, width: 940, height: 136)
        plate(c, r, radius: 24, color: 0x34489c, outline: 8)
        let inset = open ? CGRect(x: 182, y: 188, width: 796, height: 196) : CGRect(x: 182, y: 188, width: 796, height: 84)
        fill(c, rr(inset, 10), rgb(0x8b9ce0))
        if open {
            // The player's rival waits on one side, its wisp on the other.
            ghost(c, 196, 126, 0.66, tongue: true)
            wisp(c, 962, 122, 1.0)
            wisp(c, 908, 140, 0.6)
        }
    }
}
board("ghost_board_open.png", open: true)
board("ghost_board_closed.png", open: false)
print("done")
