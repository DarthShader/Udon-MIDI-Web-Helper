# Udon-MIDI-HTTP-Helper
This is a proof of concept web connectivity helper for VRChat worlds.  This external program reads the VRChat output log and looks for specific web request URLs from Udon, performs these web requests, and sends data back to VRChat through a virtual MIDI device.

# [Downloads (.exe)](https://github.com/DarthShader/Udon-MIDI-HTTP-Helper/releases)

# Requirements
* [loopMIDI by Tobias Erichsen](https://www.tobias-erichsen.de/software/loopmidi.html) - This software includes a driver for creating virtual MIDI devices, which Windows does not natively support.  loopMIDI only needs to be installed; it does not have to be running for Udon-MIDI-HTTP-Helper to function.
* Windows 10

# How to Use
* Run this program any time before entering a MIDI-enabled VRChat world.  If the program is closed after doing so, VRChat will have to be closed and re-opened in order for the MIDI connection to work again.
* Don't have other MIDI devices connected to your computer, otherwise the new [VRChat midi launch option](https://docs.vrchat.com/docs/launch-options) must be used to specify the "Udon-MIDI-HTTP-Helper" device.
* Default and extended VRChat logging is supported.

# How to Develop
To request a web page, simply use `Debug.Log("[Udon-MIDI-HTTP-Helper] " + url);` and the program will parse and return the web page as MIDI commands.
Web pages returned from servers need to be in a specific format representing midi commands: 4 integers for type/channel/note/velocity, separated by newlines for each command.
For example:
```
1 0 80 127
0 0 80 30
2 0 76 20
1 0 34 127
```

# How to Build
The program can be build with Visual Studio and the C# library wrapper included with [Tobias Erichsen's virtualMIDI SDK](http://www.tobias-erichsen.de/software/virtualmidi/virtualmidi-sdk.html)
