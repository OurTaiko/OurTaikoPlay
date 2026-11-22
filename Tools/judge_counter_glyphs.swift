// Draws the 良率 row's decimal point and percent sign of the judge counter
// (Assets/OurTaiko/Art/game/judge_counter/rate_dot.png, rate_percent.png) in the style of the
// score digits, game/lane/score_number.png: a white body with a 5 px black outline and round ends,
// in a 64 px tall cell whose glyphs sit between y 10 and the baseline at 53 (from the top). Each
// image is cropped to its ink sideways, so JudgeCounterView can set glyphs edge to edge; the
// digits' own ink is x 11 to 45 of their 56 px cells (JudgeCounterView.DigitInk).
//
// Run from the repository root (macOS):  swift Tools/judge_counter_glyphs.swift
import AppKit

let folder = "Assets/OurTaiko/Art/game/judge_counter/"
let outline: CGFloat = 5
let ink = CGColor(red: 0, green: 0, blue: 0, alpha: 1), body = CGColor(red: 1, green: 1, blue: 1, alpha: 1)

// Draws `shape` twice, grown by the outline in black and then in white, into a width × 64 image
// with y down, and writes it cropped to the columns that have ink.
func render(_ file: String, width: Int, _ shape: (CGContext, CGFloat, CGColor) -> Void) {
    let context = CGContext(data: nil, width: width, height: 64, bitsPerComponent: 8, bytesPerRow: 0,
        space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    context.translateBy(x: 0, y: 64)
    context.scaleBy(x: 1, y: -1)
    context.setLineCap(.round)
    shape(context, outline, ink)
    shape(context, 0, body)
    let image = context.makeImage()!
    let rep = NSBitmapImageRep(cgImage: image)
    var left = width, right = -1
    for x in 0..<width {
        for y in 0..<64 where rep.colorAt(x: x, y: y)!.alphaComponent > 0 {
            left = min(left, x); right = max(right, x)
        }
    }
    let cropped = NSBitmapImageRep(cgImage: image.cropping(to: CGRect(x: left, y: 0, width: right - left + 1, height: 64))!)
    try! cropped.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: folder + file))
}

func disc(_ context: CGContext, _ x: CGFloat, _ y: CGFloat, _ radius: CGFloat, _ grow: CGFloat, _ color: CGColor) {
    let r = radius + grow
    context.setFillColor(color)
    context.fillEllipse(in: CGRect(x: x - r, y: y - r, width: 2 * r, height: 2 * r))
}

// The point: an 11 px disc whose outline rests on the baseline.
render("rate_dot.png", width: 32) { context, grow, color in
    disc(context, 16, 53 - outline - 5.5, 5.5, grow, color)
}

// The percent sign: a slash between two rings, the rings' holes black like the digit 0's.
render("rate_percent.png", width: 56) { context, grow, color in
    context.setStrokeColor(color)
    context.setLineWidth(6 + 2 * grow)
    context.move(to: CGPoint(x: 39, y: 16))
    context.addLine(to: CGPoint(x: 17, y: 47))
    context.strokePath()
    disc(context, 17, 20, 6.5, grow, color)
    disc(context, 39, 43, 6.5, grow, color)
    if grow == 0 {
        disc(context, 17, 20, 2.6, 0, ink)
        disc(context, 39, 43, 2.6, 0, ink)
    }
}
