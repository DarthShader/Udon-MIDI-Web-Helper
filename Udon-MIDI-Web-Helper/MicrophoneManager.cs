using System;
using Microsoft.Win32;
using System.Security.AccessControl;

namespace Udon_MIDI_Web_Helper
{
    /*
     * This code is not used, as gaining adminstrator privileges in Windows cannot be a simple
     * opt-in system gained by running the program as administrator.  The priveleges must be 
     * listed in the application manifest, which will prompt users to allow admin access when
     * the program is first run, and that wouldn't be good for this program's trustworthiness.
     * 
     * Users are instead advised in the github readme to manually rename their microphones if
     * they want to be sure malicious worlds can't attempt to impersonate another world.
     */
    class MicrophoneManager
    {
        RegistryKey targetMicrophoneProperties;
        string originalMicrophoneName;
        const string micTypeKeyName = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
        const string micFriendlyNameKeyName = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";

        public MicrophoneManager() { }

        public void RenameMicrophone()
        {
            // Change an enabled microphone name to act as a more secure way
            // to verify the current worldID from VRChat logs
            // https://github.com/zivsha/SoundControl/blob/master/SoundControl/DefaultAudioDeviceSetter.cs#L36
            RegistryKey deviceGuids = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture", false);
            foreach (var deviceGuid in deviceGuids.GetSubKeyNames())
            {
                RegistryKey device = deviceGuids.OpenSubKey(deviceGuid);
                if (device == null) continue;
                try
                {
                    // Active devices have DeviceState=1 and three "Level:X" entries
                    if ((int)device.GetValue("DeviceState") != 1) continue;
                    if (device.GetValue("Level:0") == null) continue;
                    if (device.GetValue("Level:1") == null) continue;
                    if (device.GetValue("Level:2") == null) continue;

                    targetMicrophoneProperties = device.OpenSubKey("Properties", true); // Requires elevated permissions
                    originalMicrophoneName = (string)targetMicrophoneProperties.GetValue(micTypeKeyName);
                    byte[] bytes = new byte[8];
                    new System.Security.Cryptography.RNGCryptoServiceProvider().GetBytes(bytes);
                    string hash = BitConverter.ToString(bytes).Replace("-", "").ToLower();
                    string newMicName = originalMicrophoneName + "-" + hash;

                    string microphoneDeviceName = (string)targetMicrophoneProperties.GetValue(micFriendlyNameKeyName);
                    Console.WriteLine("Program was run as administrator.  Temporarily renaming an active microphone as an extra security measure.");
                    Console.WriteLine("Temporarily renaming microphone " + originalMicrophoneName + " (" + microphoneDeviceName + ") to " + newMicName + " (" + microphoneDeviceName + ")");
                    targetMicrophoneProperties.SetValue(micTypeKeyName, newMicName);

                    return;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.GetType().ToString() + ": " +  e.Message);
                    continue;
                }
            }

            Console.WriteLine("Program was not run as administrator.  A microphone will not be temporarily renamed.");
        }

        public void RevertMicrophoneName()
        {
            if (targetMicrophoneProperties != null)
                targetMicrophoneProperties.SetValue(micTypeKeyName, originalMicrophoneName);
        }
    }
}
