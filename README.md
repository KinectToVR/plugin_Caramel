<h1 dir=auto>
<a href="https://github.com/MochiDoesVR/Caramel-For-VRChat">Caramel</a>
<a style="color:#9966cc;" href="https://github.com/KinectToVR/Amethyst">Amethyst</a>
<text>device plugin</text>
</h1>

## **License**
This project is licensed under the GNU GPL v3 License 

## **Overview**
This repo is an implementation of the `ITrackingDevice` interface,  
providing Amethyst support for Caramel, using the [official handler code](https://github.com/MochiDoesVR/Caramel-For-VRChat).  

## **Setup**

1. Download the latest `.ipa` from [repo Releases](https://github.com/KinectToVR/plugin_Caramel/releases/latest).
2. Sideload the app using something like [Altstore](https://altstore.io)  
<br>
3. Start [Amethyst](https://www.microsoft.com/store/productId/9P7R8FGDDGDH) and install the [Caramel](https://github.com/KinectToVR/plugin_Caramel) plugin from the store ('Plugins' tab)
4. 'Refresh' the Caramel plugin, launch the iOS app and click 'Address' for discovery  
<br>
5. You're ready to go! (Probably...)

## **Downloads**
You're going to find built plugins in [repo Releases](https://github.com/KinectToVR/plugin_Caramel/releases/latest).

## **Build & Deploy**
Both build and deployment instructions [are available here](https://github.com/KinectToVR/plugin_Caramel/blob/master/.github/workflows/build.yml).
 - Open in Visual Studio and publish using the prepared publish profile  
   (`plugin_Caramel` → `Publish` → `Publish` → `Open folder`)
 - Copy the published plugin to the `plugins` folder of your local Amethyst installation  
   or register by adding it to `$env:AppData\Amethyst\amethystpaths.k2path`
   ```jsonc
   {
    "external_plugins": [
        // Add the published plugin path here, this is an example:
        "F:\\source\\repos\\plugin_Caramel\\plugin_Caramel\\bin\\Release\\Publish"
    ]
   }
   ```

## **Wanna make one too? (K2API Devices Docs)**
[This repository](https://github.com/KinectToVR/Amethyst.Plugins.Templates) contains templates for plugin types supported by Amethyst.<br>
Install the templates by `dotnet new install Amethyst.Plugins.Templates::1.2.0`  
and use them in Visual Studio (recommended) or straight from the DotNet CLI.  
The project templates already contain most of the needed documentation,  
although please feel free to check out [the official wesite](https://docs.k2vr.tech/) for more docs sometime.

The build and publishment workflow is the same as in this repo (excluding vendor deps).  