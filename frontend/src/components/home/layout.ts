// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export const capFourOnMobile = "max-md:[&>*:nth-child(n+5)]:hidden";
export const capFiveOnMobile = "max-md:[&>*:nth-child(n+6)]:hidden";

export const deferredSection = "[content-visibility:auto] [contain-intrinsic-size:auto_18rem]";

// Списки треков лежат на карточке, полки с обложками — прямо на фоне: так главная не сливается в одну ленту.
export const listPanel = "rounded-xl bg-card p-5 max-md:-mx-1 max-md:p-3";
