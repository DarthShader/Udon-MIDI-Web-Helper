# SlotPool
This is a per-player object pooling system where each player 'claims' a 'slot' GameObject upon joining a world.  The slots in the pool can have any number of child GameObjects with synced Udon behaviors that represent per-player data.  The modularity of these 'slot data' behaviors means they can be added to worlds that need different per-player data, and each behavior can be manually synced at different intervals to efficiently use network bandwidth.

# Advantages
Additionally, this technique has unique benefits over existing object pooling systems like Phasedragon's Simple Object Pool or CyanLaser's CyanPlayerObjectPool:
* Data objects can be added modularly to the pool in different worlds, depending on what systems are in use.
* Serializing behavior data for network transport individually means data can be sent faster (continuous and manual sync methods work).
* Player-targeted network events are very easy to make using `NetworkEventTarget.Owner`, since each player owns a Slot behavior.
* Network events with arguments are possible - a SlotData behavior can sync data which represents the function arguments.  This does incur the overhead of sending the 'event' to all clients, but it is extremely easy to work with.
* This system is entirely event driven, with no costly update overhead.
* SlotData behaviors are easily built on an example SlotData UdonSharp class (may be changed to an inheritance model when UdonSharp 1.0 fully releases).
* No hacky workarounds - there are no periodic cleanups or delayed checks to accomodate for VRChat's unreliable networking.  Ownership transfer improvements and the addition of manual sync from the Udon networking update allows SlotPool to work deterministically.
