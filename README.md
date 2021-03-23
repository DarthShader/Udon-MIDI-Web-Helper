# Udon-MIDI-Web-Helper
This is a terms-of-service abiding web connectivity helper for VRChat worlds.  This external program reads the VRChat output log and looks for specific web request URLs from Udon, performs these web requests, and sends data back to VRChat through a virtual MIDI device.  Up to 255 simultaneous HTTP and WebSocket connections can be made, and on average data can be transferred through MIDI at ~100kbps.

# [Downloads (.exe and .unitypackage)](https://github.com/DarthShader/Udon-MIDI-HTTP-Helper/releases)

# Requirements
* [loopMIDI by Tobias Erichsen](https://www.tobias-erichsen.de/software/loopmidi.html) - This software includes a driver for creating virtual MIDI devices, which Windows does not natively support.  loopMIDI only needs to be installed; it does not have to be running for Udon-MIDI-HTTP-Helper to function.
* Windows 10

# How to Use
* Run this program any time before starting VRChat or before entering a MIDI-enabled VRChat world.  If the program is closed after doing so, VRChat will have to be closed and re-opened in order for the MIDI connection to work again.
* Don't have other MIDI devices connected to your computer, otherwise the new [VRChat midi launch option](https://docs.vrchat.com/docs/launch-options) must be used to specify the "Udon-MIDI-HTTP-Helper" device.
* Default and extended VRChat logging is supported.

# How to Develop
Requires [UdonSharp](https://github.com/MerlinVR/UdonSharp)

Refer to the provided examples for how to make web requests and open WebSocket connections in Udon.

# How to Build
The program can be build with Visual Studio and the C# library wrapper included with [Tobias Erichsen's virtualMIDI SDK](http://www.tobias-erichsen.de/software/virtualmidi/virtualmidi-sdk.html)
