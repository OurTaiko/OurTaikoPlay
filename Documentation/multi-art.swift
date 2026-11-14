// Draws OurTaikoPlay's マルチ演奏 art. Usage: swift main.swift <output folder>
// Everything is drawn here from shapes; no existing artwork is read.
import AppKit
import CoreGraphics
import Foundation

let out = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "out")
try? FileManager.default.createDirectory(at: out.appendingPathComponent("stamps"), withIntermediateDirectories: true)

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

// ---------------------------------------------------------------- room background

draw("room_bg.png", 1920, 1080) { c in
    let all = CGRect(x: 0, y: 0, width: 1920, height: 1080)
    gradient(c, CGPath(rect: all, transform: nil), [rgb(0x1b1147), rgb(0x3a1568), rgb(0x7a1f5c)], CGPoint(x: 0, y: 0), CGPoint(x: 0, y: 1080))
    // Waves of overlapping arcs along the lower third.
    for row in 0..<7 {
        let y = 760 + CGFloat(row) * 54, shift: CGFloat = row % 2 == 0 ? 0 : 60
        for i in -1..<18 {
            let x = CGFloat(i) * 120 + shift
            for (k, radius) in [58, 40, 22].enumerated() {
                stroke(c, CGPath(ellipseIn: CGRect(x: x - CGFloat(radius), y: y - CGFloat(radius), width: CGFloat(radius) * 2, height: CGFloat(radius) * 2), transform: nil),
                       rgb(0xffffff, 0.035 + 0.012 * CGFloat(k)), 5)
            }
        }
    }
    // Lantern glows.
    var seed: UInt64 = 20261114
    func next() -> CGFloat { seed = seed &* 6364136223846793005 &+ 1442695040888963407; return CGFloat((seed >> 33) % 10000) / 10000 }
    let glowColors: [UInt32] = [0xff6b3d, 0xffc93d, 0x4fd6ff, 0xff5fa2, 0x9bff6b]
    for _ in 0..<46 {
        let x = next() * 1920, y = next() * 620, r = 26 + next() * 90, col = glowColors[Int(next() * 4.99)]
        radial(c, CGPoint(x: x, y: y), r, [rgb(col, 0.20 + next() * 0.14), rgb(col, 0)])
    }
    // A string of lanterns across the top.
    let rope = CGMutablePath(); rope.move(to: CGPoint(x: -20, y: 60))
    rope.addQuadCurve(to: CGPoint(x: 1940, y: 60), control: CGPoint(x: 960, y: 190))
    stroke(c, rope, rgb(0x000000, 0.55), 5)
    for i in 0..<15 {
        let t = (CGFloat(i) + 0.5) / 15, x = -20 + 1960 * t
        let y = (1 - t) * (1 - t) * 60 + 2 * (1 - t) * t * 190 + t * t * 60
        let col: UInt32 = i % 2 == 0 ? 0xff5a36 : 0xffd23d
        radial(c, CGPoint(x: x, y: y + 34), 80, [rgb(col, 0.35), rgb(col, 0)])
        let body = CGRect(x: x - 22, y: y + 8, width: 44, height: 54)
        fill(c, rr(body, 20), black)
        fill(c, rr(body.insetBy(dx: 4, dy: 4), 16), rgb(col))
        c.setFillColor(rgb(0xffffff, 0.35)); c.fill(CGRect(x: x - 12, y: y + 16, width: 6, height: 30))
        fill(c, rr(CGRect(x: x - 12, y: y + 2, width: 24, height: 9), 3), black)
        fill(c, rr(CGRect(x: x - 12, y: y + 59, width: 24, height: 9), 3), black)
    }
    // A soft floor shade so panels read against it.
    gradient(c, CGPath(rect: all, transform: nil), [rgb(0, 0), rgb(0, 0.35)], CGPoint(x: 0, y: 700), CGPoint(x: 0, y: 1080))
}

// ---------------------------------------------------------------- nine-slice pieces

// A dark panel: border 36.
draw("panel.png", 96, 96) { c in
    let r = CGRect(x: 4, y: 4, width: 88, height: 88)
    fill(c, rr(r, 26), black)
    fill(c, rr(r.insetBy(dx: 5, dy: 5), 21), rgb(0x1a1436, 0.94))
    stroke(c, rr(r.insetBy(dx: 7.5, dy: 7.5), 19), rgb(0xffffff, 0.16), 2)
}

// A white plate to tint: buttons, cards. Border 30.
draw("plate.png", 96, 96) { c in
    plate(c, CGRect(x: 6, y: 4, width: 84, height: 82), radius: 24, color: 0xffffff)
}

// The lighter inset of a plate (the skin's boards have one). Border 20.
draw("inset.png", 64, 64) { c in
    fill(c, rr(CGRect(x: 2, y: 2, width: 60, height: 60), 14), rgb(0xffffff))
}

// The focus ring around a plate. Border 36.
draw("focus.png", 120, 120) { c in
    let r = CGRect(x: 14, y: 14, width: 92, height: 92)
    c.saveGState(); c.setShadow(offset: .zero, blur: 12, color: rgb(0xfff200, 0.95))
    stroke(c, rr(r, 28), rgb(0xfff200), 9); c.restoreGState()
    stroke(c, rr(r, 28), rgb(0xffff9a), 4)
}

// A rounded bar to tint (gauges). Border 10.
draw("bar.png", 32, 24) { c in
    fill(c, rr(CGRect(x: 0, y: 0, width: 32, height: 24), 12), white)
}

// A speech bubble for stamps: the tail points down-left. Border l34 r34 t34 b52.
draw("bubble.png", 120, 132) { c in
    let body = CGRect(x: 6, y: 6, width: 108, height: 96)
    let tail = poly([(30, 96), (24, 126), (58, 98)])
    c.saveGState(); c.setShadow(offset: CGSize(width: 0, height: -4), blur: 6, color: rgb(0, 0.4))
    fill(c, rr(body, 28), black); fill(c, tail, black); c.restoreGState()
    stroke(c, tail, black, 10)
    fill(c, rr(body.insetBy(dx: 5, dy: 5), 23), white)
    fill(c, poly([(34, 94), (30, 116), (52, 96)]), white)
}

// A ribbon to tint, for MVP and SUPER STAR. Border l40 r40.
draw("ribbon.png", 160, 64) { c in
    let tailL = poly([(0, 14), (38, 14), (38, 58), (0, 58), (16, 36)])
    let tailR = poly([(160, 14), (122, 14), (122, 58), (160, 58), (144, 36)])
    for p in [tailL, tailR] { fill(c, p, black) }
    fill(c, poly([(8, 19), (36, 19), (36, 53), (8, 53), (21, 36)]), rgb(0xc8c8c8))
    fill(c, poly([(152, 19), (124, 19), (124, 53), (152, 53), (139, 36)]), rgb(0xc8c8c8))
    plate(c, CGRect(x: 22, y: 2, width: 116, height: 50), radius: 10, color: 0xffffff, outline: 5, shadow: false)
}

// ---------------------------------------------------------------- icons

// A white disc with the skin's outline, to tint: avatars, rank badges.
draw("disc.png", 128, 128) { c in
    fill(c, circle(64, 64, 62), black)
    fill(c, circle(64, 64, 55), white)
    c.saveGState(); c.addPath(circle(64, 64, 55)); c.clip()
    c.setFillColor(rgb(0, 0.14)); c.fillEllipse(in: CGRect(x: -6, y: 70, width: 140, height: 90))
    c.restoreGState()
}

func crown(_ c: CGContext, _ box: CGRect, _ colors: [CGColor], jewels: CGColor) {
    let x = box.minX, y = box.minY, w = box.width, h = box.height
    let shape = poly([(x, y + h * 0.28), (x + w * 0.26, y + h * 0.58), (x + w * 0.5, y + h * 0.08), (x + w * 0.74, y + h * 0.58),
                      (x + w, y + h * 0.28), (x + w * 0.88, y + h * 0.95), (x + w * 0.12, y + h * 0.95)])
    stroke(c, shape, black, 10); fill(c, shape, black)
    gradient(c, shape, colors, CGPoint(x: x, y: y), CGPoint(x: x + w, y: y + h))
    c.saveGState(); c.addPath(shape); c.clip()
    c.setFillColor(rgb(0xffffff, 0.45)); c.fill(CGRect(x: x, y: y + h * 0.72, width: w, height: h * 0.07))
    c.restoreGState()
    for (px, py) in [(x, y + h * 0.28), (x + w * 0.5, y + h * 0.08), (x + w, y + h * 0.28)] {
        fill(c, circle(px, py, w * 0.085), black); fill(c, circle(px, py, w * 0.05), jewels)
    }
}
draw("crown_host.png", 72, 64) { c in crown(c, CGRect(x: 10, y: 8, width: 52, height: 46), [rgb(0xffe45c), rgb(0xffb300)], jewels: rgb(0xff4d6d)) }
draw("crown_clear.png", 72, 64) { c in crown(c, CGRect(x: 10, y: 8, width: 52, height: 46), [rgb(0xf4f6fa), rgb(0x9aa7b8)], jewels: rgb(0x6fd3ff)) }
draw("crown_fc.png", 72, 64) { c in crown(c, CGRect(x: 10, y: 8, width: 52, height: 46), [rgb(0xfff07a), rgb(0xff9d00)], jewels: rgb(0xff3d3d)) }
draw("crown_dfc.png", 72, 64) { c in
    crown(c, CGRect(x: 10, y: 8, width: 52, height: 46), [rgb(0xff5f8f), rgb(0xffd34d), rgb(0x61e88a), rgb(0x4fc3ff), rgb(0xb47bff)], jewels: white)
}

draw("check.png", 64, 64) { c in
    fill(c, circle(32, 32, 30), black); fill(c, circle(32, 32, 25), rgb(0x2fc45a))
    let tick = CGMutablePath(); tick.move(to: CGPoint(x: 18, y: 33)); tick.addLine(to: CGPoint(x: 28, y: 43)); tick.addLine(to: CGPoint(x: 46, y: 22))
    stroke(c, tick, white, 8)
}

draw("wait.png", 64, 64) { c in
    fill(c, circle(32, 32, 30), black); fill(c, circle(32, 32, 25), rgb(0x6a6f85))
    for i in 0..<3 { fill(c, circle(18 + CGFloat(i) * 14, 32, 5), white) }
}

draw("offline.png", 64, 64) { c in
    fill(c, circle(32, 32, 30), black); fill(c, circle(32, 32, 25), rgb(0xe0453a))
    let a = CGMutablePath(); a.move(to: CGPoint(x: 21, y: 21)); a.addLine(to: CGPoint(x: 43, y: 43))
    a.move(to: CGPoint(x: 43, y: 21)); a.addLine(to: CGPoint(x: 21, y: 43))
    stroke(c, a, white, 8)
}

draw("lock.png", 64, 64) { c in
    let shackle = CGMutablePath(); shackle.addArc(center: CGPoint(x: 32, y: 26), radius: 12, startAngle: .pi, endAngle: 0, clockwise: false)
    shackle.addLine(to: CGPoint(x: 44, y: 34)); shackle.move(to: CGPoint(x: 20, y: 26)); shackle.addLine(to: CGPoint(x: 20, y: 34))
    stroke(c, shackle, black, 13); stroke(c, shackle, rgb(0xd9dde6), 6)
    fill(c, rr(CGRect(x: 10, y: 28, width: 44, height: 32), 9), black)
    fill(c, rr(CGRect(x: 14, y: 32, width: 36, height: 24), 6), rgb(0xffc531))
    fill(c, circle(32, 42, 4.5), black); fill(c, rr(CGRect(x: 30, y: 42, width: 4, height: 9), 2), black)
}

draw("open.png", 64, 64) { c in
    fill(c, circle(32, 32, 28), black); fill(c, circle(32, 32, 23), rgb(0x35b8ff))
    c.saveGState(); c.addPath(circle(32, 32, 23)); c.clip()
    stroke(c, CGPath(ellipseIn: CGRect(x: 20, y: 6, width: 24, height: 52), transform: nil), white, 3)
    let lines = CGMutablePath(); lines.move(to: CGPoint(x: 6, y: 32)); lines.addLine(to: CGPoint(x: 58, y: 32))
    lines.move(to: CGPoint(x: 32, y: 6)); lines.addLine(to: CGPoint(x: 32, y: 58))
    lines.move(to: CGPoint(x: 10, y: 20)); lines.addQuadCurve(to: CGPoint(x: 54, y: 20), control: CGPoint(x: 32, y: 27))
    lines.move(to: CGPoint(x: 10, y: 44)); lines.addQuadCurve(to: CGPoint(x: 54, y: 44), control: CGPoint(x: 32, y: 37))
    stroke(c, lines, white, 3); c.restoreGState()
}

draw("clock.png", 64, 64) { c in
    fill(c, circle(32, 34, 27), black); fill(c, circle(32, 34, 21), white)
    fill(c, rr(CGRect(x: 26, y: 2, width: 12, height: 9), 3), black)
    let hands = CGMutablePath(); hands.move(to: CGPoint(x: 32, y: 21)); hands.addLine(to: CGPoint(x: 32, y: 34)); hands.addLine(to: CGPoint(x: 42, y: 40))
    stroke(c, hands, rgb(0xe0453a), 5)
}

// A small drum seen from the front: the mode's own mark.
func drum(_ c: CGContext, _ x: CGFloat, _ y: CGFloat, _ s: CGFloat, face: UInt32, rim: UInt32) {
    fill(c, circle(x, y, 50 * s), black)
    fill(c, circle(x, y, 44 * s), rgb(rim))
    fill(c, circle(x, y, 33 * s), black)
    fill(c, circle(x, y, 29 * s), rgb(face))
    c.saveGState(); c.addPath(circle(x, y, 29 * s)); c.clip()
    c.setFillColor(rgb(0xffffff, 0.35)); c.fillEllipse(in: CGRect(x: x - 34 * s, y: y - 44 * s, width: 60 * s, height: 44 * s))
    c.restoreGState()
}
draw("mark.png", 128, 128) { c in
    drum(c, 44, 78, 0.78, face: 0xff5a36, rim: 0xfff3d6)
    drum(c, 84, 50, 0.78, face: 0x45c8e8, rim: 0xfff3d6)
}

// ---------------------------------------------------------------- Entry's mode board (1160x460, as the skin's)

func board(_ name: String, open: Bool) {
    draw(name, 1160, 460) { c in
        let r = open ? CGRect(x: 110, y: 55, width: 940, height: 350) : CGRect(x: 110, y: 162, width: 940, height: 136)
        plate(c, r, radius: 24, color: 0x8a4fd8, outline: 8)
        let inset = open ? CGRect(x: 182, y: 188, width: 796, height: 196) : CGRect(x: 182, y: 188, width: 796, height: 84)
        fill(c, rr(inset, 10), rgb(0xbd97ee))
        if open {
            // Two drums either side of the title: the two sides of a room.
            drum(c, 190, 122, 0.62, face: 0xff5a36, rim: 0xfff3d6)
            drum(c, 970, 122, 0.62, face: 0x45c8e8, rim: 0xfff3d6)
        }
    }
}
board("entry_board_open.png", open: true)
board("entry_board_closed.png", open: false)

// ---------------------------------------------------------------- stamps: sixteen faces

enum Eyes { case dots, happy, wink, wide, closed, cry, angry, stars, hearts, spiral }
enum Mouth { case smile, grin, open, flat, frown, wow, tongue, wavy, cat }
enum Extra { case none, sweat, tears, anger, zzz, sparkle, note, fire, question, bang, blush, hands }

func face(_ index: Int, _ color: UInt32, _ eyes: Eyes, _ mouth: Mouth, _ extra: Extra) {
    draw("stamps/\(index).png", 160, 160) { c in
        let cx: CGFloat = 80, cy: CGFloat = 84, r: CGFloat = 60
        // The face is a drum head: a rim, then the skin.
        fill(c, circle(cx, cy, r + 8), black)
        fill(c, circle(cx, cy, r + 1), rgb(0xfff3d6))
        fill(c, circle(cx, cy, r - 9), black)
        fill(c, circle(cx, cy, r - 14), rgb(color))
        c.saveGState(); c.addPath(circle(cx, cy, r - 14)); c.clip()
        c.setFillColor(rgb(0xffffff, 0.28)); c.fillEllipse(in: CGRect(x: cx - 58, y: cy - 70, width: 100, height: 64))
        c.restoreGState()
        if extra == .blush {
            fill(c, circle(cx - 30, cy + 12, 9), rgb(0xff6b8f, 0.75)); fill(c, circle(cx + 30, cy + 12, 9), rgb(0xff6b8f, 0.75))
        }
        let ex: CGFloat = 19, ey = cy - 8
        func eye(_ x: CGFloat, left: Bool) {
            switch eyes {
            case .dots: fill(c, circle(x, ey, 7), black)
            case .wide:
                fill(c, circle(x, ey, 10), black); fill(c, circle(x, ey, 6.5), white); fill(c, circle(x, ey, 3), black)
            case .happy:
                let p = CGMutablePath(); p.addArc(center: CGPoint(x: x, y: ey + 4), radius: 8, startAngle: .pi, endAngle: 0, clockwise: false)
                stroke(c, p, black, 5.5)
            case .closed:
                let p = CGMutablePath(); p.move(to: CGPoint(x: x - 8, y: ey)); p.addLine(to: CGPoint(x: x + 8, y: ey)); stroke(c, p, black, 5.5)
            case .wink:
                if left { fill(c, circle(x, ey, 7), black) } else {
                    let p = CGMutablePath(); p.move(to: CGPoint(x: x - 8, y: ey - 4)); p.addLine(to: CGPoint(x: x + 6, y: ey)); p.addLine(to: CGPoint(x: x - 8, y: ey + 4))
                    stroke(c, p, black, 5)
                }
            case .cry:
                let p = CGMutablePath(); p.addArc(center: CGPoint(x: x, y: ey - 2), radius: 8, startAngle: 0, endAngle: .pi, clockwise: false)
                stroke(c, p, black, 5.5)
            case .angry:
                fill(c, circle(x, ey + 2, 6.5), black)
                let p = CGMutablePath()
                p.move(to: CGPoint(x: x + (left ? -11 : 11), y: ey - 13)); p.addLine(to: CGPoint(x: x + (left ? 9 : -9), y: ey - 5))
                stroke(c, p, black, 5.5)
            case .stars: fill(c, star(x, ey, 12, 5), black); fill(c, star(x, ey, 8, 3.4), rgb(0xfff27a))
            case .hearts:
                let p = CGMutablePath(); p.move(to: CGPoint(x: x, y: ey + 9))
                p.addCurve(to: CGPoint(x: x, y: ey - 4), control1: CGPoint(x: x - 18, y: ey - 2), control2: CGPoint(x: x - 7, y: ey - 14))
                p.addCurve(to: CGPoint(x: x, y: ey + 9), control1: CGPoint(x: x + 7, y: ey - 14), control2: CGPoint(x: x + 18, y: ey - 2))
                stroke(c, p, black, 5); fill(c, p, rgb(0xff3d6e))
            case .spiral:
                let p = CGMutablePath(); p.move(to: CGPoint(x: x, y: ey))
                for i in 1...40 { let a = CGFloat(i) * 0.42, rad = CGFloat(i) * 0.24; p.addLine(to: CGPoint(x: x + cos(a) * rad, y: ey + sin(a) * rad)) }
                stroke(c, p, black, 3.5)
            }
        }
        eye(cx - ex, left: true); eye(cx + ex, left: false)
        let my = cy + 20
        switch mouth {
        case .smile:
            let p = CGMutablePath(); p.addArc(center: CGPoint(x: cx, y: my - 8), radius: 15, startAngle: .pi * 0.15, endAngle: .pi * 0.85, clockwise: false); stroke(c, p, black, 5.5)
        case .grin:
            let p = CGMutablePath(); p.move(to: CGPoint(x: cx - 18, y: my - 6)); p.addLine(to: CGPoint(x: cx + 18, y: my - 6))
            p.addQuadCurve(to: CGPoint(x: cx - 18, y: my - 6), control: CGPoint(x: cx, y: my + 26)); p.closeSubpath()
            stroke(c, p, black, 5); fill(c, p, rgb(0xb3202a))
            c.saveGState(); c.addPath(p); c.clip(); c.setFillColor(white); c.fill(CGRect(x: cx - 18, y: my - 6, width: 36, height: 6)); c.restoreGState()
        case .open:
            fill(c, CGPath(ellipseIn: CGRect(x: cx - 11, y: my - 8, width: 22, height: 24), transform: nil), black)
            fill(c, CGPath(ellipseIn: CGRect(x: cx - 6.5, y: my + 2, width: 13, height: 10), transform: nil), rgb(0xff5d6c))
        case .flat:
            let p = CGMutablePath(); p.move(to: CGPoint(x: cx - 13, y: my)); p.addLine(to: CGPoint(x: cx + 13, y: my)); stroke(c, p, black, 5.5)
        case .frown:
            let p = CGMutablePath(); p.addArc(center: CGPoint(x: cx, y: my + 14), radius: 15, startAngle: .pi * 1.15, endAngle: .pi * 1.85, clockwise: false); stroke(c, p, black, 5.5)
        case .wow:
            fill(c, circle(cx, my + 2, 9), black); fill(c, circle(cx, my + 2, 4.5), rgb(0xb3202a))
        case .tongue:
            let p = CGMutablePath(); p.addArc(center: CGPoint(x: cx, y: my - 8), radius: 15, startAngle: .pi * 0.15, endAngle: .pi * 0.85, clockwise: false); stroke(c, p, black, 5.5)
            let t = rr(CGRect(x: cx - 2, y: my + 3, width: 13, height: 15), 6); stroke(c, t, black, 4.5); fill(c, t, rgb(0xff6b8f))
        case .wavy:
            let p = CGMutablePath(); p.move(to: CGPoint(x: cx - 16, y: my))
            p.addCurve(to: CGPoint(x: cx + 16, y: my), control1: CGPoint(x: cx - 6, y: my - 12), control2: CGPoint(x: cx + 6, y: my + 12)); stroke(c, p, black, 5.5)
        case .cat:
            let p = CGMutablePath(); p.addArc(center: CGPoint(x: cx - 8, y: my - 6), radius: 8, startAngle: 0, endAngle: .pi, clockwise: false)
            p.move(to: CGPoint(x: cx + 16, y: my - 6)); p.addArc(center: CGPoint(x: cx + 8, y: my - 6), radius: 8, startAngle: 0, endAngle: .pi, clockwise: false)
            stroke(c, p, black, 5)
        }
        switch extra {
        case .none, .blush: break
        case .sweat:
            let p = CGMutablePath(); p.move(to: CGPoint(x: 128, y: 34))
            p.addQuadCurve(to: CGPoint(x: 128, y: 66), control: CGPoint(x: 108, y: 62)); p.addQuadCurve(to: CGPoint(x: 128, y: 34), control: CGPoint(x: 148, y: 62))
            stroke(c, p, black, 5); fill(c, p, rgb(0x6fd3ff))
        case .tears:
            for x in [cx - ex, cx + ex] {
                let p = rr(CGRect(x: x - 6, y: ey + 4, width: 12, height: 40), 6); stroke(c, p, black, 4.5); fill(c, p, rgb(0x6fd3ff))
            }
        case .anger:
            let p = CGMutablePath()
            for (dx, dy) in [(-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)] {
                p.move(to: CGPoint(x: 128 + CGFloat(dx) * 15, y: 34 + CGFloat(dy) * 5)); p.addQuadCurve(to: CGPoint(x: 128 + CGFloat(dx) * 5, y: 34 + CGFloat(dy) * 15), control: CGPoint(x: 128 + CGFloat(dx) * 5, y: 34 + CGFloat(dy) * 5))
            }
            stroke(c, p, black, 10); stroke(c, p, rgb(0xff3d3d), 5)
        case .zzz:
            for (i, s) in [(0, 18.0), (1, 13.0), (2, 9.0)] {
                let x = 112 + CGFloat(i) * 16, y = 44 - CGFloat(i) * 15, k = CGFloat(s)
                let p = CGMutablePath(); p.move(to: CGPoint(x: x, y: y)); p.addLine(to: CGPoint(x: x + k, y: y)); p.addLine(to: CGPoint(x: x, y: y + k)); p.addLine(to: CGPoint(x: x + k, y: y + k))
                stroke(c, p, black, 7); stroke(c, p, white, 3)
            }
        case .sparkle:
            for (x, y, s) in [(130.0, 30.0, 16.0), (26.0, 36.0, 12.0), (136.0, 118.0, 10.0)] {
                fill(c, star(CGFloat(x), CGFloat(y), CGFloat(s) + 4, CGFloat(s) * 0.3 + 3, 4), black)
                fill(c, star(CGFloat(x), CGFloat(y), CGFloat(s), CGFloat(s) * 0.28, 4), rgb(0xfff27a))
            }
        case .note:
            let p = CGMutablePath(); p.move(to: CGPoint(x: 124, y: 56)); p.addLine(to: CGPoint(x: 124, y: 22)); p.addLine(to: CGPoint(x: 146, y: 16)); p.addLine(to: CGPoint(x: 146, y: 48))
            stroke(c, p, black, 9); fill(c, circle(118, 56, 10), black); fill(c, circle(140, 48, 10), black)
            stroke(c, p, rgb(0xff5fa2), 4); fill(c, circle(118, 56, 6.5), rgb(0xff5fa2)); fill(c, circle(140, 48, 6.5), rgb(0xff5fa2))
        case .fire:
            for (x, s) in [(28.0, 1.0), (132.0, 1.0)] {
                let bx = CGFloat(x), k = CGFloat(s)
                let p = CGMutablePath(); p.move(to: CGPoint(x: bx, y: 20))
                p.addCurve(to: CGPoint(x: bx, y: 62), control1: CGPoint(x: bx + 26 * k, y: 40), control2: CGPoint(x: bx + 18 * k, y: 62))
                p.addCurve(to: CGPoint(x: bx, y: 20), control1: CGPoint(x: bx - 18 * k, y: 62), control2: CGPoint(x: bx - 22 * k, y: 44))
                stroke(c, p, black, 6); gradient(c, p, [rgb(0xffe45c), rgb(0xff5a1f)], CGPoint(x: bx, y: 62), CGPoint(x: bx, y: 20))
            }
        case .question, .bang:
            let p = CGMutablePath()
            if extra == .question {
                p.addArc(center: CGPoint(x: 130, y: 30), radius: 12, startAngle: .pi, endAngle: .pi * 0.5, clockwise: false)
                p.addLine(to: CGPoint(x: 130, y: 52))
            } else { p.move(to: CGPoint(x: 130, y: 14)); p.addLine(to: CGPoint(x: 130, y: 50)) }
            stroke(c, p, black, 13); fill(c, circle(130, 66, 8.5), black)
            stroke(c, p, rgb(extra == .question ? 0x6fd3ff : 0xff3d3d), 6.5); fill(c, circle(130, 66, 5), rgb(extra == .question ? 0x6fd3ff : 0xff3d3d))
        case .hands:
            for x in [22.0, 138.0] {
                fill(c, circle(CGFloat(x), 122, 17), black); fill(c, circle(CGFloat(x), 122, 12), rgb(0xfff3d6))
            }
        }
    }
}
let red: UInt32 = 0xff5a36, blue: UInt32 = 0x45c8e8, yellow: UInt32 = 0xffc93d, green: UInt32 = 0x61d67a, pink: UInt32 = 0xff7fb0, violet: UInt32 = 0xa884f0
face(0, red, .dots, .smile, .hands)        // hello
face(1, blue, .happy, .grin, .none)        // nice to play with you
face(2, yellow, .stars, .grin, .sparkle)   // great!
face(3, green, .wink, .smile, .note)       // let's go
face(4, pink, .hearts, .smile, .blush)     // thank you
face(5, red, .wide, .wow, .bang)           // wow
face(6, blue, .cry, .frown, .tears)        // so close
face(7, violet, .closed, .flat, .sweat)    // sorry
face(8, yellow, .happy, .tongue, .none)    // easy!
face(9, red, .angry, .wavy, .fire)         // fired up
face(10, green, .dots, .flat, .question)   // which song?
face(11, blue, .closed, .cat, .zzz)        // sleepy / wait a moment
face(12, pink, .happy, .cat, .blush)       // good game
face(13, violet, .spiral, .wavy, .sweat)   // too hard
face(14, yellow, .wide, .open, .sparkle)   // full combo!
face(15, green, .happy, .smile, .hands)    // one more
print("done")
