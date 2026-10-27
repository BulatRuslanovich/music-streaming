// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

fn main() {
    println!("cargo:rerun-if-changed=../../../db/migrations");
}
