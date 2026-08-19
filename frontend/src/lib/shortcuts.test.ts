import { describe, expect, it } from "vitest";
import { NUDGE_STEP, SEEK_STEP, SHORTCUT_VOLUME_STEP, resolveShortcut } from "@/lib/shortcuts";

function press(
  key: string,
  modifiers: Partial<Record<"shift" | "alt" | "command", boolean>> & { code?: string } = {},
) {
  return resolveShortcut({
    key,
    code: modifiers.code,
    shiftKey: modifiers.shift ?? false,
    altKey: modifiers.alt ?? false,
    ctrlKey: modifiers.command ?? false,
    metaKey: false,
  });
}

describe("resolveShortcut", () => {
  it("toggles playback on space and k", () => {
    expect(press(" ")).toEqual({ action: "playPause" });
    expect(press("k")).toEqual({ action: "playPause" });
  });

  it("seeks with the arrows and nudges with j and l", () => {
    expect(press("ArrowRight")).toEqual({ action: "seekBy", value: SEEK_STEP });
    expect(press("ArrowLeft")).toEqual({ action: "seekBy", value: -SEEK_STEP });
    expect(press("l")).toEqual({ action: "seekBy", value: NUDGE_STEP });
    expect(press("j")).toEqual({ action: "seekBy", value: -NUDGE_STEP });
  });

  it("changes track with shifted arrows", () => {
    expect(press("ArrowRight", { shift: true })).toEqual({ action: "next" });
    expect(press("ArrowLeft", { shift: true })).toEqual({ action: "previous" });
  });

  it("jumps to a percentage of the track", () => {
    expect(press("0")).toEqual({ action: "seekPercent", value: 0 });
    expect(press("7")).toEqual({ action: "seekPercent", value: 70 });
  });

  it("changes volume with plus and minus in both shift states", () => {
    expect(press("+", { shift: true })).toEqual({
      action: "volumeBy",
      value: SHORTCUT_VOLUME_STEP,
    });
    expect(press("=")).toEqual({ action: "volumeBy", value: SHORTCUT_VOLUME_STEP });
    expect(press("-")).toEqual({ action: "volumeBy", value: -SHORTCUT_VOLUME_STEP });
  });

  it("opens the palette only with the command key", () => {
    expect(press("k", { command: true })).toEqual({ action: "palette" });
    expect(press("K", { command: true, shift: true })).toEqual({ action: "palette" });
    expect(press("p", { command: true })).toBeNull();
  });

  it("opens the help overlay on a question mark", () => {
    expect(press("?", { shift: true })).toEqual({ action: "help" });
  });

  it("stays out of the way of browser shortcuts", () => {
    expect(press("ArrowLeft", { alt: true })).toBeNull();
    expect(press("r", { command: true })).toBeNull();
    expect(press("s", { shift: true })).toBeNull();
  });

  it("reads the physical key when the layout is not latin", () => {
    expect(press("\u043b", { command: true, code: "KeyK" })).toEqual({ action: "palette" });
    expect(press("\u0430", { code: "KeyF" })).toEqual({ action: "favorite" });
    expect(press("\u043e", { code: "KeyJ" })).toEqual({ action: "seekBy", value: -NUDGE_STEP });
    expect(press("\u043b", { code: "KeyK" })).toEqual({ action: "playPause" });
  });

  it("keeps punctuation on the produced character, not the physical key", () => {
    expect(press("?", { shift: true, code: "Digit7" })).toEqual({ action: "help" });
    expect(press("7", { code: "Digit7" })).toEqual({ action: "seekPercent", value: 70 });
  });

  it("ignores keys it does not know", () => {
    expect(press("x")).toBeNull();
    expect(press("Escape")).toBeNull();
  });
});
