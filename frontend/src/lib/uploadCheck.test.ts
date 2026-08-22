// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { beforeEach, describe, expect, it, vi } from "vitest";
import type { UploadProbeFile, UploadProbeResult } from "./types";

const checkUpload = vi.fn<(files: UploadProbeFile[]) => Promise<UploadProbeResult>>();

vi.mock("./api", () => ({
  api: { checkUpload: (files: UploadProbeFile[]) => checkUpload(files) },
}));
vi.mock("./audioTags", () => ({ readAudioTags: () => Promise.resolve({}) }));
vi.mock("./fileHash", () => ({ sha256File: () => Promise.resolve("a".repeat(64)) }));

const { checkAgainstLibrary, fileKey, isDuplicate } = await import("./uploadCheck");

function audio(name: string): File {
  return new File(["something"], name);
}

function allNew(files: UploadProbeFile[]): UploadProbeResult {
  return {
    files: files.map((file) => ({ fileName: file.fileName, verdict: "New", basis: "Hash" })),
  };
}

describe("checkAgainstLibrary", () => {
  beforeEach(() => {
    checkUpload.mockReset();
  });

  it("splits a queue the server would refuse into batches it accepts", async () => {
    checkUpload.mockImplementation((files) => Promise.resolve(allNew(files)));

    const files = Array.from({ length: 260 }, (_, index) => audio(`${index}.mp3`));
    const checks = await checkAgainstLibrary(files);

    expect(checkUpload.mock.calls.map(([sent]) => sent.length)).toEqual([250, 10]);
    expect(Object.keys(checks)).toHaveLength(260);
    expect(Object.values(checks).every((one) => one.state === "checked")).toBe(true);
  });

  it("reports files as unchecked rather than new when the check fails", async () => {
    checkUpload.mockRejectedValue(new Error("offline"));

    const file = audio("song.mp3");
    const checks = await checkAgainstLibrary([file]);

    expect(checks[fileKey(file)]).toEqual({ state: "failed" });
  });

  it("keeps the batches that answered when one of them fails", async () => {
    checkUpload
      .mockImplementationOnce((files) => Promise.resolve(allNew(files)))
      .mockRejectedValueOnce(new Error("offline"));

    const files = Array.from({ length: 251 }, (_, index) => audio(`${index}.mp3`));
    const checks = await checkAgainstLibrary(files);

    expect(checks[fileKey(files[0])].state).toBe("checked");
    expect(checks[fileKey(files[250])]).toEqual({ state: "failed" });
  });

  it("does not pass off a short answer as a verdict on every file", async () => {
    checkUpload.mockResolvedValue({
      files: [{ fileName: "one.mp3", verdict: "New", basis: "Hash" }],
    });

    const files = [audio("one.mp3"), audio("two.mp3")];
    const checks = await checkAgainstLibrary(files);

    expect(checks[fileKey(files[0])].state).toBe("checked");
    expect(checks[fileKey(files[1])]).toEqual({ state: "failed" });
  });

  it("carries the match and the basis the server reported", async () => {
    checkUpload.mockResolvedValue({
      files: [
        { fileName: "dupe.mp3", verdict: "Duplicate", basis: "Hash" },
        { fileName: "bare.mp3", verdict: "New", basis: "None" },
      ],
    });

    const files = [audio("dupe.mp3"), audio("bare.mp3")];
    const checks = await checkAgainstLibrary(files);

    expect(isDuplicate(checks[fileKey(files[0])])).toBe(true);
    expect(checks[fileKey(files[1])]).toEqual({
      state: "checked",
      verdict: "New",
      basis: "None",
      match: null,
    });
  });
});
