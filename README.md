# Apple Music Animated Artworks

![Build Status](https://github.com/m8tec/apple-music-animated-artworks/actions/workflows/docker-publish.yml/badge.svg)

A lightweight tool to fetch and display Apple Music’s animated album covers (HLS/m3u8). Built with .NET 10 and a minimal Tailwind CSS frontend.

## What it does
- Scraping: Pulls the .m3u8 stream URL directly from Apple Music’s public web player using JSON-LD metadata.
- Persistent Cache: Saves results in a local cache_database.json file. It only hits Apple's servers once per album.
- Thread Safety: Uses a keyed locker to prevent multiple concurrent requests for the same album from overloading the backend.
- Web Player: Simple UI using hls.js to play the animated covers in any browser (not just Safari).

## Tech Stack
- Backend: .NET 10 (Minimal APIs, HttpClient, Regex for parsing)
- Frontend: Plain JS, Tailwind CSS, Hls.js
- Storage: Simple JSON-based persistence (In-memory dictionary + file flush)

## 🛠 API Reference

**Get Artwork by Details**

```GET /api/v1/artwork?artist=Linkin+Park&album=Living+Things```

**Get Artwork by URL**

```GET /api/v1/artwork/by-url?url=https://music.apple.com/us/album/...```

**Get Global History**

```GET /api/v1/artwork/history```

## 💾 Caching Strategy
This project is designed to be "Apple-friendly" by minimizing outgoing requests:
1. Request Received: Check RAM dictionary for the Apple Music URL.
2. Cache Miss: Check cache_database.json on disk.
3. Fetching: If still not found, fetch from Apple.
4. Persist: Write results (including "Not Found" flags) to the JSON database immediately.

## ⚖️ Legal Disclaimer
This project is for educational purposes only. It uses web scraping techniques to retrieve publicly available metadata. Please respect Apple Music's Terms of Service and use this tool responsibly.