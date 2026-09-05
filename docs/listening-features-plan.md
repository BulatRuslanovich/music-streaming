# Listening features implementation plan

## Outcome

Deliver monthly listening stories, Caimack Connect between open browser clients,
and optional loudness normalization and track transitions. Keep original audio
files intact and preserve existing playback, offline downloads, and privacy.

## 1. Monthly recap

- Serve the last completed calendar month in the listener's configured time zone,
  with explicit start and exclusive end boundaries. The month is never chosen.
- Open the recap only during the first seven days of a new month, in that same
  zone. Outside the window it does not exist: the API answers 404 and the client
  drops the route, the navigation entry, and the home banner.
- Return listening totals, ranked tracks and artists, first recorded artist
  discoveries, and a comparison with the preceding calendar month.
- Lay the page out like any other opened entity — tinted hero, headline figures,
  ranked sections — with playback, saving a normal playlist, and a downloadable
  summary image drawn from the month's own covers.
- Announce it with a dismissible banner on the home page that carries no numbers.
- Check the window boundaries, month boundaries, user isolation, discovery
  history, and empty months.

## 2. Caimack Connect

- Register open clients with names, expiring presence, and playback snapshots.
- Scope every device and command to the authenticated listener.
- Support play, pause, next, previous, seek, volume, and transferring the queue,
  play order, repeat mode, and current position to another open client.
- Show devices and remote controls in a responsive player dialog, including
  when the local player has no track.
- Handle unavailable devices, command expiration, browser playback rejection,
  and reconnects without replaying stale commands.
- Check isolation, expiration, invalid commands, and transfer behavior.

## 3. Audio

- Add optional track/album loudness normalization with conservative gain limits.
- Add adjustable crossfade and a separately selectable gapless playback path.
- Preserve album dynamics with a common album gain. Do not rewrite originals.
- Keep playback controls, queue order, repeat, seek, and telemetry consistent
  across transitions; cancel preparation on queue/source changes.
- Fall back to existing streaming when transition preparation is unavailable.
- Check gain math, transition scheduling/cancellation, and existing player tests.

## Verification and delivery

- Run frontend type checking, lint, formatting, and tests; build the frontend.
- Build and test the backend, including integration tests when Docker is available.
- Check source license headers and inspect the final diff.
- Record implementation status and any verified platform limitations below.

## Status

- Implemented all three features. No database migration or new environment
  variable is required.
- Monthly recap lives at `/recap` during the first seven days of each month and
  always covers the previous one; there is no month picker. In that window it is
  reachable from a dismissible home banner, a navigation entry, and the
  statistics page; outside it the route redirects home and the API returns 404.
  It uses existing listening history, supports private playlist creation, and
  exports a PNG card built from the month's covers and the current theme.
  Discoveries mean first appearances in the recorded history, not the listener's
  first encounter with an artist anywhere.
- Connect is available from the device button in the player, including its idle
  state. Open clients poll every two seconds; presence expires after 30 seconds
  and commands after 10 seconds. It transfers queue/order/repeat/position and
  playback state; destination volume stays local. Positions are approximate.
  Clients must belong to the same account, and browser autoplay restrictions
  still apply. This is control of open web clients, not multiroom synchronization
  or integration with third-party speakers. Registry state is in memory and
  assumes a single API process; clients re-register after a server restart.
- Sound settings are under Settings → Playback and default to off. Track and
  album normalization use cached FFmpeg full-recording loudness/true-peak
  measurements, a -16 LUFS target, a -2 dB true-peak ceiling, and bounded gain.
  An album uses a common gain. Missing analysis leaves gain unchanged. First
  analysis can take time; original files are never rewritten.
- Gapless/crossfade schedule decoded audio on the Web Audio clock. Crossfade
  duration is adjustable from 1 to 12 seconds. Preparation uses additional
  bandwidth/memory and falls back to normal playback for offline/slow networks,
  data saver, recordings over 10 minutes, unsupported decoding, or buffer limits
  (32 MiB compressed / 128 MiB decoded per track). Safari/iOS and physical device
  handoff have not been verified; Chromium was exercised locally.
- Fixed two existing service-worker faults encountered by the browser checks:
  cached home HTML was used for other online routes, and partial audio responses
  were sent through the JSON cache. Navigation now prefers the network with an
  offline fallback; progressive audio bypasses the data cache.
- Fixed the monthly discovery LINQ translation failure by filtering the scalar
  artist projection before constructing `StatisticsEntryDto`. The PostgreSQL
  regression covers calendar boundaries, account isolation, known co-artists,
  new artists, empty months, and saved playlist order/privacy.
- Verification: backend 637 tests passed; frontend 231 tests passed; production
  build and TypeScript passed. ESLint has no errors and four existing image
  warnings. Browser checks cover remote controls/transfer, both transitions and
  pause, recap export, and ordinary playback. Source formatting and license
  headers are checked before delivery.
