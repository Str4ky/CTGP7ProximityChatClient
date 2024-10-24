# CTGP-7 Proximity Chat
The proximity chat functionality in the [CTGP-7 modpack](https://ctgp-7.github.io) allows all players running the proximity chat client to hear each other in race while playing online. Voices will be adjusted depending on how far you are, as well as a few other surprises. ;)

## Note from Str4ky
This fork don't work of many bugs that could occur and also that I don't have the Vivox API key that Pablo uses in his release so to play this you're gonna have to install Pablo's version. This repo is just for reworking the UI and make it to the default program (Pablo's one)

This application runs on a Windows or Linux computer and is available on both the Nintendo 3DS and Citra versions of CTGP-7.

## Download
You can get the latest version from the [releases page](https://github.com/PabloMK7/CTGP7ProximityChatClient/releases/latest).

## Usage
### Setup
1) Download the proximity chat client and run the executable in the computer that is also running CTGP-7 on Citra, or is in the same network as the real Nintendo 3DS. **If promted, give the application network access so it can connect to the 3DS/Citra.**
2) In CTGP-7, select CTGP-7 Network and make sure to enable proximity chat in the public/private lobby selection screen.
3) When asked, enter the IP address shown in the proximity chat client window (if you are using Citra, you can leave it as `127.0.0.1`). You can verify the connection was successful if the status indicator turns green.
4) Join any online room, and if other players are also using proximity chat, you will get connected together.

### Controls
- **Volume:** Adjusts the master volume of all the users.
- **Mic:** Changes the input device and adjusts its volume.
- **Doppler:** Changes how much the other player voices change depending on their velocity.

Furthermore, you can adjust the volume of each participant using the slider next to their names.

## Credits
Voice chat functionality is provided by [Vivox](https://vivox.com/).
