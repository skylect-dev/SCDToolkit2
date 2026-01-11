# SCDToolkit

A Windows audio player and toolkit designed for game music files (SCD format) with Kingdom Hearts Randomizer integration. Also plays WAV, MP3, OGG, and FLAC files.


### SCDToolkit has a [Discord](https://discord.gg/FqePtT2BBM) now!

## Features

- **Game Audio Support**: Native SCD file playback from Kingdom Hearts, Final Fantasy XIV, and other games
- **Library Management**: Organize files by folder with search and filtering
- **Kingdom Hearts II Randomizer Integration**: Export music directly to Kingdom Hearts Randomizer folders
- **Kingdom Hearts II Re:Fined Music Pack Creation:** Directly create custom Music Pack mods to be used with Kingdom Hearts II - Re:Fined. 
- **Format Conversion**: Convert between MANY file formats
- **Loop Editor**: Accurate waveform editor for setting precise loop points
- **Auto-Updates**: Built-in update system keeps you current


## Batch Normalization: ## 
Normalize volume levels across multiple files directly from the library. This will apply a consistent loudness level to all selected tracks, ensuring a uniform listening experience while in-game.
- **Auto Normalization**: Intelligent volume normalization based on audio characteristics. Targets -12 LUFS with True Peak of -1 dBTP.
- **SCD HEX Float Patching**: Applies float patch directly to SCD files to ensure in-game volume consistency.
- **Caching**: Processed files are cached for faster future access. Run a batch normalization to pre-cache files and speed up exporting.
    - Cached files are automatically reused unless source files change.
    - Cached files export faster since they skip re-processing.

## Installation

**Download & Run (Recommended):**
1. Get the latest version from [Releases](https://github.com/skylect-dev/SCDToolkit/releases)
2. Extract `SCDToolkit-Windows.zip`
3. Run `SCDToolkit.exe`

Everything needed is included - no additional setup required.

## Quick Start

1. Launch SCDToolkit
2. Click "Add Folder" to add your music folders
3. Double-click any file to play
4. Use the search box to find specific tracks
5. Select files and use conversion/export buttons as needed

Your library and settings are automatically saved.


## Acknowledgments

Thanks to the KHReFined, KHRando, and OpenKH Discord communities for feedback and suggestions!
Special thanks to TopazTK for support and suggestions!
Special thanks to PunningLinguist for identifying the vgmstream loop issue!

## License

MIT License
