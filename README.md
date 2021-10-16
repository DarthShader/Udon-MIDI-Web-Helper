![icon](https://user-images.githubusercontent.com/42289116/112239883-b4314480-8c1d-11eb-812a-329190c426af.png)

# Udon-MIDI-Web-Helper
This is a terms-of-service abiding web connectivity helper for VRChat worlds.  This external program reads the VRChat output log and looks for specific web request URLs from Udon, performs these web requests, and sends data back to VRChat through a virtual MIDI device.  Up to 255 simultaneous HTTP and WebSocket connections can be made, and on average data can be transferred through MIDI at ~100kbps.  It also comes with local storage options for worlds, so persistence can be achieved without needing a web server.

# [Downloads (.exe and .unitypackage)](http://github.com/DarthShader/Udon-MIDI-Web-Helper/releases)

# Requirements
* [loopMIDI by Tobias Erichsen](https://www.tobias-erichsen.de/software/loopmidi.html) - This software includes a driver for creating virtual MIDI devices, which Windows does not natively support.  loopMIDI only needs to be installed; it does not have to be running for Udon-MIDI-Web-Helper to function.
* Windows 10

# How to Use
* Run this program any time before starting VRChat or before entering a MIDI-enabled VRChat world.  If the program is closed after doing so, VRChat will have to be closed and re-opened in order for the MIDI connection to work again.
* Don't have other MIDI devices connected to your computer, otherwise the new [VRChat midi launch option](https://docs.vrchat.com/docs/launch-options) must be used to specify the "Udon-MIDI-Web-Helper" device.
* Default and extended VRChat logging is supported.  However, in order to use web based authentication, **you must launch VRChat with at least --log-debug-levels=API**

# Known Limitations & Vulnerabilities
* The entire protocol for this system is built on the expectation that VRChat will instantly crash if it receives more than 256 bytes of MIDI data in a signle frame.  Thus your actual throughput is always tied to your framerate.
* You cannot use other MIDI devices in any VRChat world if you launched the game with this program.
* Worlds with Udon behaviors that allow users to print arbitrary text to the output log are at risk of malicious users injecting helper program commands.  This is an even greater risk if these behaviors use synced strings.
* Secure verification of the current world ID from the output log (useful for making sure worlds don't maliciously overwrite each others' local data or send malicious web requests to servers) uses **your active microphone list** as a fingerprint.  **It is recommended to change at least one active microphone on your computer to a unique, unguessable name - like a password** - so malicious worlds cannot attempt to impersonate a different world and potentially overwrite your saved data.
* Security keys are generated in a local data folder for each new web server that wants to authenticate you as a unique user.  You should treat the generated `.keys` file as a collection of local passwords.  If you lose that file, web servers will not be able to authenticate you and you will potentially lose remove user data.
* VRChat worlds have the power to make any web requests/WebSocket connections they want, as long as they are targeting public internet IP addresses.  Depending on world behavior, your individual IP address is at risk of having its reputation lowered and being blacklisted by certain web authorities.  However, all web requests are logged, so malicious worlds are easy to pinpoint.

# How to use in VRChat worlds
Requires [UdonSharp] (https://github.com/MerlinVR/UdonSharp) - currently requires the latest 1.0 beta version available in the discord server

Currently requires the latest Open Beta build of VRChat (version 1137+)

To add web conectivity to an Udon powered VRChat world, add a single copy of the provided prefab `UdonMIDIWebHandler`.  This contains a single UdonBehaviour through which all web connections and midi data passes.  

To make HTTP GET requests through UdonSharpBehaviours: 
1. Link a public variable to the singleton `UdonMIDIWebHandler`
2. Add the variables `int connectionID`, `byte[] connectionData`, `string connectionString`, `int responseCode` to the behaviour
3. Call `WebRequestGet()` on the handler, which returns an int ID for the connection that is opening.  This is useful for behaviours that need to open multiple connections.
4. Implement a `public void WebRequestReceived()` function that the handler can call.  Before calling, it populates the `connectionID`, `connectionData`, `connectionString`, and `responseCode` variables.

To use WebSocket connections through UdonSharpBehaviours:
1. Link a public variable to the singleton `UdonMIDIWebHandler`
2. Add the variables `int connectionID`, `byte[] connectionData`, `string connectionString`, `bool messageIsText` to the behaviour
3. Call `WebSocketOpen()` on the handler, which returns a connection ID
4. Implement the functions `public void WebSocketReceive()` and `public void WebSocketClosed()` which populate the previously listed variables before being called by the handler

To use Local Storage Persistence through UdonSharpBehaviours:
1. Link a public variable to the singleton `UdonMIDIWebHandler`
2. Add the variables `int connectionID`, `byte[] connectionData`, `string connectionString`, `bool messageIsText` to the behaviour
3. Call `StoreLocalValue()` on the handler to store key/value string pairs
4. Call `RetrieveLocalValue()` on the handler to retrieve a key/value string pair
5. To receive retrieved values, implement a `public void WebRequestReceived()` function that the handler can call.  Before calling, it populates the `connectionID`,  `connectionString`, and `responseCode` variables.  `connectionString` is the string value, and `responseCode` is an HTTP-like response code for whether or not the retrieve was successful or not

More detailed examples for these systems are available in the provided unitypackage.

# How to Build
The program can be built with Visual Studio and the C# library wrapper included with [Tobias Erichsen's virtualMIDI SDK](http://www.tobias-erichsen.de/software/virtualmidi/virtualmidi-sdk.html)
