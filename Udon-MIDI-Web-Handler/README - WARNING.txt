WARNING: As world authors, it is YOUR responsibility to not allow any arbitrary 
user-created strings to be printed to the output log by UdonBehaviours, especially 
if they are synced strings.  New lines (lines ending wih \n\n\r\n) could be spoofed
to the output log which appear as legitimate Udon-MIDI-Web-Handler commands originating 
from the world.  Player data for your world could be erased or overwritten if 
this is exploited.