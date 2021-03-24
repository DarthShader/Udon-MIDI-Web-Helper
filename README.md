![icon](https://user-images.githubusercontent.com/42289116/112239883-b4314480-8c1d-11eb-812a-329190c426af.png)

# Udon-MIDI-Web-Helper
This is a terms-of-service abiding web connectivity helper for VRChat worlds.  This external program reads the VRChat output log and looks for specific web request URLs from Udon, performs these web requests, and sends data back to VRChat through a virtual MIDI device.  Up to 256 simultaneous HTTP and WebSocket connections can be made, and on average data can be transferred through MIDI at ~100kbps.

# [Downloads (.exe and .unitypackage)](https://github.com/DarthShader/Udon-MIDI-HTTP-Helper/releases)

# Requirements
* [loopMIDI by Tobias Erichsen](https://www.tobias-erichsen.de/software/loopmidi.html) - This software includes a driver for creating virtual MIDI devices, which Windows does not natively support.  loopMIDI only needs to be installed; it does not have to be running for Udon-MIDI-HTTP-Helper to function.
* Windows 10

# How to Use
* Run this program any time before starting VRChat or before entering a MIDI-enabled VRChat world.  If the program is closed after doing so, VRChat will have to be closed and re-opened in order for the MIDI connection to work again.
* Don't have other MIDI devices connected to your computer, otherwise the new [VRChat midi launch option](https://docs.vrchat.com/docs/launch-options) must be used to specify the "Udon-MIDI-HTTP-Helper" device.
* Default and extended VRChat logging is supported.

# How to use in VRChat worlds
Requires [UdonSharp](https://github.com/MerlinVR/UdonSharp)

To add web conectivity to an Udon powered VRChat world, add a single copy of the provided prefab `UdonMIDIWebHandler`.  This contains a single UdonBehaviour through which all web connections and midi data passes.  

To make HTTP GET requests through UdonSharpBehaviours: 
1. Link a public variable to the singleton `UdonMIDIWebHandler`
2. Add the public variables `int connectionID`, `byte[] connectionData`, `int responseCode` to the behaviour
3. Call `WebRequestGet(uri, this)` on the handler, which returns an int ID for the connection that is opening.  This is useful for behaviours that need to open multiple connections.
4. Implement a `public void WebRequestGetCallback()` function that the handler can call.  Before calling, it populates the `connectionID`, `connectionData`, and `responseCode` variables.

To use WebSocket connections through UdonSharpBehaviours:
1. Link a public variable to the singleton `UdonMIDIWebHandler`
2. Add the public variables `int connectionID`, `byte[] connectionData`, `bool messageIsText` to the behaviour
3. Call `WebSocketOpen(uri, this)` on the handler, which returns a connection ID
4. Implement the functions `public void WebSocketReceive()` and `public void WebSocketClosed()` which populate the previously listed variables before being called by the handler

# How to Build
The program can be build with Visual Studio and the C# library wrapper included with [Tobias Erichsen's virtualMIDI SDK](http://www.tobias-erichsen.de/software/virtualmidi/virtualmidi-sdk.html)
