using System;
using System.Collections.Generic;

namespace OurTaiko
{
    public enum TjaLineKind
    {
        Header,  // KEY:VALUE outside a chart block
        Start,   // #START, with its player (P1, P2) as Value
        End,     // #END
        Body,    // a note line or command inside #START … #END
        Other,   // anything else outside a block
    }

    // One meaningful line of a TJA file: without the BOM, `//` comments or surrounding spaces;
    // empty lines are skipped. #START and #END are recognised in any case.
    public readonly struct TjaLine
    {
        public readonly TjaLineKind Kind;
        public readonly string Text;
        // A header's upper-case key and trimmed value; #START's player.
        public readonly string Key, Value;

        public TjaLine(TjaLineKind kind, string text, string key = null, string value = null)
        {
            Kind = kind; Text = text; Key = key; Value = value;
        }
    }

    // The one line reader behind TjaParser, SongInfo, the local song search and the online
    // playable copy, so they agree on comments, case and block boundaries.
    public static class TjaLines
    {
        public static IEnumerable<TjaLine> Read(string text)
        {
            bool inBlock = false;
            foreach (string raw in text.TrimStart('﻿').Replace("\r", "").Split('\n'))
            {
                int comment = raw.IndexOf("//", StringComparison.Ordinal);
                string line = (comment >= 0 ? raw.Substring(0, comment) : raw).Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#START", StringComparison.OrdinalIgnoreCase))
                {
                    inBlock = true;
                    yield return new TjaLine(TjaLineKind.Start, line, value: line.Substring(6).Trim().ToUpperInvariant());
                }
                else if (line.Equals("#END", StringComparison.OrdinalIgnoreCase))
                {
                    inBlock = false;
                    yield return new TjaLine(TjaLineKind.End, line);
                }
                else if (inBlock) yield return new TjaLine(TjaLineKind.Body, line);
                else
                {
                    int colon = line.IndexOf(':');
                    yield return colon < 0 ? new TjaLine(TjaLineKind.Other, line)
                        : new TjaLine(TjaLineKind.Header, line, line.Substring(0, colon).Trim().ToUpperInvariant(), line.Substring(colon + 1).Trim());
                }
            }
        }
    }
}
