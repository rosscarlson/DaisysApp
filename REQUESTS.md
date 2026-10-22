# Requests

What was asked in each session, word for word (the session notes add to it; never condensed). Search it for when
something was asked for.

## 2026-10-19

Module: Claude - feature time - while I want you to update README.md and CLAUDE.md after you execute a command I give you (also store the command I gave you so it can be searched later, if you're not already) you don't need to show me that, you can just show a colapsed line that indicates it happened, it's wasting too much screen space. Also if you do that and you're replying bit information - like after you do a commit/publish - then put it BEFORE that text as it makes it harder to read it by it scrolling off the page. Also in the right column where it says in red "Everything is allowed....." remove that text and instead when the box is checked change "Allow Everything" to red (and change the text to just "Allow Everything")

When complete publish as the next sub minor version, so 2.0.1

Module - Claude/Main: I see "Claude 94%" in the header notification area since I'm near the limit - however I only want that to show when the AI is actively working, if it's not actively processing I don't need to see it in the header. Publish as 2.0.2 when complete

Hardware Module - Processes - need to add a column for network activity, as a percentage of all traffic, again just like windows task manager. When ready publish as 2.1

well I see the column but there is NO data in it at all - it's just empty and if I sort by it or scroll I see nothing - give it another try and publish as 2.1.1 when ready

Claude Module - when pasting text from Windows Notepad several times the text turned black on a very dark background like it was formatted code or an image. If I took the text and pasted into say the Start - Run box and then copied it back to the clipboard it removed that formatting and pasted fine in the app. So not sure if a bug on our end or not - I want the box to support rich text, images (like you added) so let's add that (do NOT add spell checking, I don't need to be reminded how bad I am!) Also in the display stream whenever you send the user output, with the light blue left color and blue background, in the bottom right corner of the window add a timestamp, but in a relatively small font. ONLY put the timestamps here.

Claude Modules Settings: Let's create 2 columns for the various settings blocks with the example area taking the entire width (so 2 columns by 3 rows).

Gaming Module - Mini Mirror - when a friend creates a mirror it basically does a capture of the moment it's created then there is no video, ever. It WAS working for him fine until a few versions back so we're not sure when or what changed it (pre 1.9 or something like that, very hard to say). What can we do to fix or provide you for testing? If you have code changes please publish with a new sub minor version.

Video Module - Overlay - the medium overlay isn't aligned well, the right side of the bars isn't in line and right justified as the amount of VRAM is too much text and is pushing the bars over. It says 11.5/15.9 - so let's round the second number so in my case 16 so it'll llook much better and make sure it's all left and right justified - publish with new sub minor version when complete

Gaming Module - Joy2Key submodule - first we're going to rename to "Button2Key", please update everywhere including the settings page. Then we'll refer to it as "Butt2Key" everwyere but the main title/tab - feature change, right now I can assign a specific process to the profile but one profile may work with multiple games so it should add it to a list vs just filling in the one field.

Once complete update to next minor version (2.2, 2.3, 2.4, etc) and publish

Video Module - Overlay - let's also left justify the left column so it's right against the graphs on the right, which are all lined up with each other perfectly - when done publish as next sub minor version

Video Module - Recording sub module - when the virtual webcam is running we should update the titlebar with a green notificaiont like the others. Then add a settings popup to set the hotkeys/bindings for start/stop the virtual webcam (1 button) and to mute/unmute that output. Then a third button that mutes/unmutes the camera overlays but leaves the virtual webcam running so it'll stream my screen but NOT my actual cameras.

Once complete update to next minor version (2.2, 2.3, 2.4, etc) and publish

are you doing anything?

Gaming - Butt2Key and Forza Wheel - if they were active when the app was closed, say for an update, they should return to their active states on launch and vice versa.

Gaming - Forza Wheel - just like the profiles in Butt2Key we should be able to add certain processes that if running ForzaWheel auto starts and when they exit it auto stops. So a new box just below Hush that let's you select a running process and add it to a list of processes that auto-activate the profile.

When fixed publish as next sub minor version.

## 2026-10-20

Module: Claude - right now we can connect to a single remote DA client to control it, I'd like to be able to connect to more than 1, so we need a system to add/remove/edit connections so when we start a new session we can choose which "server" to connect to and control. When ready update to 2.4 and publish.

Module: Claude - we need the ability to switch between Claude accounts, so the box in the bottom right should include a drop down to select the active account with a "switch account" button that would prompt me to pick which account I want active, then we'll need the ability in settings to add multiple accounts - in the future we may support other accounts types other than Claude (if we don't already) so make sure that's part of the process.

Publish with new minor version (so 2.x) when ready

Module: Claude - the "Claude Code" box in the bottom right, following changes:
- Rename to "Claude Account"
- Remove "Sign Out", "Add an account" and "Check Again"
- Remove "Switch Account", instead just switch if they select a different account in the dropdown box
- Rename "Usage details" to just "Usage"

Publish as 2.5.1 when ready

Module: Claude - on the "Add a PC" page let's change the 4 fields to "Name" "IP Address" "Port" and "Passphrase" - all should be right justified with a bit of space between them and the field. Then put the Test button to the left of the Save button.

Module: Claude - let's remove the usage details at the top that are duplicated on the right bar - then we need to update that right bar such that if we're connected to a remote session it shows the details for that remote session's usage data and not local, only when using a local session would you see the local session limits, etc. 

In the Claude Account let's only show the account label rather than the email so the "Usage" button is in line with the drop down.

Publish with new subversion (so 2.x.x) when ready

Module: Video - Submodule: Mini Mirror - when a friend tries to create a mirror it basically takes a static snapshot at the moment he creates it and that's it, it's never live video, just that moment in time. He is using this successfully using the tool you also built before Mini Mirror that we took the code from so we know it *can* work on his PC. He's on similar to me, 4070Ti (vs my 5070Ti) but same CPU, RAM, etc. OS - I'll make sure he's on the latest video driver - but are there things you need to check and if so can you build that into the app so that it logs to our logging system? Or if you think/find a bug publish a build with the next subversion number.

Nope, still not working - I just dropped a zip file in the Downloads directory for you to look at - it just gives him a frozen black box. If the log doesn't show the issue let's add detailed logging so we can track this down. Then publish as 2.6.1

New Module - Butt2Key Remote
This module will allow the user to create "ButtScreens" (our name for the virtual screen/display) for a remote device, either tablet, phone or PC, to allow the user to presses a virtual button on the remote device and part of this module completes the appropriate action. There should be a tool that when popped open will let you lay out the buttons on a virtual screen with the user able to select from several standard layout types (4:3, 2:1, other phone/tablet ratios) with the ability to enter the exact pixel count of the device (assuming you don't have a better approach which if you do is fine) - they would define a grid size that would then define the size of the buttons, those should have rounded or square corners (a setting when creating the button) with the ability to change the outline, background colors as well as font face, size and color. The user can then move the new button around to affect the layout how they want with buttons being able to be resized when created/edited to 1x2, 2x2, 2x1, etc. on the grid. The virtual screens that a user creates should also be able to be "popped out" and used with the mouse (or a touchscreen PC) just like the would on the mobile device with the ability to add transparency, lock position and keep on top. When creating a button the user can supply an icon but I'd like a library of icons they can choose from with as many as you can provide so long as they are all in the same style with those icons being able to be "colored", to match the colors used for the buttons. This isn't a per-button setting but a per-page setting (other than a per-button icon/ability to upload one). There is also a list of the already created ButtScreens to be edited or deleted.

The "ButtScreens" would then be pulled by a remote client, Android first, so we'll need a passphrase setting for auth - that client would then display the selected layout(s) and send what button was pressed to the "server" being this app. So we'll need to be listening on a specific port for that traffic and process appropriately. The server will advertise things like available layouts, names, etc. as appropriate.

When creating new buttons in this layout tool it should work exactly like Button2Key does now, with macros, etc. and with as similar an interface as makes sense. We would also have profiles for games/apps with the same process watching feature as we have in Button2Key setting a group of ButtScreens as active so you can create BigButts to store multiple ButtScreens (please use these terms in the UI as appropriate).

We'll wait until I'm happy with the interface/server piece before we create the brief for the 2nd half of the project, the Android client. We'll obviously architect this in such a way that any client could be built, likely iOS next I just don't own one to test with.

Publish as new 2.x when I can test.

continue please

## 2026-10-21

When deleting a ButtScreen just change the button to "confirm?" after clicking delete where clicking the 2nd time confirms it. In fact update the main README so that in the future any button that requires confirmation works this way.

BigButts isn't quite right - we'd want to be able to cycle through a collection of ButtScreens in a group, or Big Butt then that BigButt would be assigned a process, like we do with Button2Key now (looking the same way, so that each profile gets a different set of possible processes that enable that profile). So it's exactly the same as profiles, we're just calling a profile a "BigButt" and that can have multiple ButtScreens. The client will handle moving from screen to screen, you'll need to tell it how many screens in this BigButt, etc.

Let's also default to it off - and when it's running we need to put the green notification in the titlebar. As well as a hotkey binding next to the "On" button to be able to toggle it on/off.

Module: Claude - New block on the right column, above "Claude Account" - any active remote sessions from any remote clients you have setup will show there and clicking on it will open to that as a new/current tab so you can quickly connect/control remote sessions that are already running. 

When ready publish with new sub version

Module: Butt2Key Remote
New Project: Butt2Key Remote Android Client
Details: Build and deploy an Android app that will integreate with Butt2Key remote. It should allow for login to a specific DA client (so we'll need Name, IP, Port and Passphrase) then once connected it would show you all the BigButts available by showing you the various profiles that have been built. Based on which BigButt is active in DA that is the one that will display on the Andorid client. This should have an option to run full screen and in that mode we'll need a small "Exit" button on screen (with a setting in the app to choose which corner it should be in). This PC is connected to an Android device via USB that Claude Code has worked with before so you should be able to deploy to that for testing - ultimately I'm going to want an APK that I can deploy on various devices.

If there are obvious features I'm leaving out feel free based on what we've built in B2KR (Butt2Key Remote) that the client would need to offer full features. We're basically replicating the app GameGlass with a little bit of StreamDeck combined.

If you make any changes to Daisy's App for this publish a new subversion and if possible at this point drop the APK in my downloads or I can test on the connected device (a Samsung Galaxy Tab 2), my other testing device will be a Pixel 10 Pro (let's offer a profile for that in the B2KR Daisy's App as well.

Module: Button2Key Remote
Issue: We need to add a "Save & Close" button in the top right (and remove the text "double-lick an empty cell...." and put the button on this line, right justified.
Issue: In the BigButts block we need a way to delete and rename BigButts (profiles)
Issue: The Devices block with port and phassphrase needs a "save" button. Then format, below that, like this:
Active Devices
- Pixel10Pro (Android 17, 192.168.1.92)
- Pixel6Pro (Android 17, 192.168.1.90)
With no other text in the box
Module: Button2Key Remote Android Client
Issue: When going to full screen we need to add a border around the buttons as on my Pixel 10 Pro the top right corner is hidden by the curved corner of the phone display.
Issue: When connected to a PC it should list the various BigButts that are available so the user can select which one they want to use right now.
Feature: Check for updates in the android client with the ability to update from the app so once installed you don't need to manually install the APK anymore
When complete update both the main app and APK and publish both with new subversion.

It seems you skipped 2 of the items I asked for, they are below.

Daisy's App
Module: Button2Key Remote
Issue: In the BigButts block we need a way to delete and rename BigButts, this block should also be above the "Default" block with that name "Default" changing to the name of the active BigButt. Then in the BigButts block we should be able to add processes that would have it auto switch to that BigButt (and make sure B2KR is running if that process is running) - this should work just like it does in B2K and ForzaWheel

Android App
Module: Button2Key Remote Android Client
Issue: When connected to a PC it should list the various BigButts that are available so the user can select which one they want to use right now.

When complete update both the main app and APK and publish both with new subversion (so 1.1.1 for the android app).

Please continue where you left off.

Module: Butt2Key Remote
Issue: When adding a new ButtScreen if you switch between portrait and landscape auto swap the grid column/rows
Issue: When selecting a non-active BigButt I see the rename/delete buttons in the box with the list, it needs to be down with "New" and "Copy", just a bug. Remove "Default can't be......." don't need that text.
Issue: When you select different BigButts the ButtScreens on the right should be changing - as those are part of the BigButt (thinking of that as a profile, we're just using a funny name). So you create a new BigButt named "New" and on the right under ButtScreens you'd see nothing. You'd then create new ButtScreens there and when you switch to a different BigButt you'd no longer see those ButtScreens. However when creating a new ButtScreen you should be able to "link" to an existing one from a different BigButt such that if that changes it changes in all BigButts.

When complete update to version 2.7.6 and publish

Bug: When I click the different profiles I lose the ability to add a process as it seems you're treating default as something special, there shouldn't need to be one, but without a profile it wouldn't have anything to present to a remote client so we nee dto be able to say "no BigButts", etc. to remote clients. Fix this bug - you'll see 2 images in my Downloads directory showing you exactly what's happening.
Issue: Clicking on a ButtScreen should open the edit screen
Feature: On the ButtScreen edit page in the Colours box let's add a "Theme" dropdown above the existing colors. Then give me a few choices for themes ("butt" like names, as in the human buttock/ass, would be hysterical)

Mobile App
Bug: on the main page if I select a server to connect to I see the BigButts on that machine but this page forces the UI into landscape and it should NOT as we haven't yet selected a BigButt to actually display.

When ready update both app subversion number and publish

Module: Butt2Key Remote
New Feature: When creating a ButtScreen and/or BigButt I'd love a template that would create an appropriate set of buttons with appropatei icons/labels for that app, so the user goes in an edits as necessary. If you can include the icon and button press by looking up the details for the following games:
- Forza Horizon (5 & 6 specifically)
- iRacing
- Star Citizen
- Elite Dangerous
- Assetto Corsa Competitizone
Let's create those templates as json files such that a user could drop a new one in a specific folder (or import into the app, so we'll need that button on the settings page) that template would be available to them. You can decide the column/row count for each of these as some might not need so many buttons, etc. Finally after you've selected a template there should be a short wizard that lets the user select sections/groups of buttons and the individual buttons within. So for example in Elite Dangerous you'd have things like landing gear, cargo rack, lights - they would likely be in a group where things like weapons, etc. would be in their groups. Either way you would then show the specific buttons for each group as well, everything with check marks in an interface that can expand/collapse. Then after they've selected what they need those buttons would be added such that if 2 or more groups can fit on a ButtScreen they would otherwise they'd push to the next screen with the user able to move them as they see fit editing later.
Issue: Remove the next "None in default yet......." in the main window
when ready publish with new minor version (so 2.8, etc)

ButtScreens from a template changes
- Title: change to "ButtScreen Template"
- let's give the user the choice in a grid or larger "button" styled selections with the check boxes rather than a file tree smaller view
- Rename "Make the ButtScreen" to "Create"
- In the wizard we need to start by asking them what device so it gets the right layout, we should also ask for a theme at the end with a view of what it will look like when they hit the final create, so the page we see now has "next", then the next/final screen has "Create"
When editing a ButtScreen we need a check box just under the label field (so left justified with the label field over on the right) with the words "Show Text" and when checked whatever is in the Label field is shown on the button, if it's unticked then no text is shown, only the icon. Then in the "Colours" box, rename that to "Style" and add a row "Titles" and on the left 2 buttons "Show" & "Hide" that would show and hide all buttons indepdenetly of what's set on each, this would stay green when enabled and just gray when not so you can override all individual buttons when you want. Also remove the text "A theme colours.....", "the grid sets the....." and "A device that has....." and "Sizes are in the...." as well. Rename all the goofy names to something more seriously, that didn't work for me.
Feature: In the Size dropdown we have 2 listings for Pixel 10 Pro (Pixel10Pro and "Google Pixel 10 Pro") - I like the longer name better. Also we need to add iPhone layouts for everything other than iPhone Duo is the phones have different diemsions, if not just iPhone is fine.
Publish as 2.8.1 when ready

Module: Butt2Key
Rename: "From a template" to "Butt Wizard", "Link a ButtScreen" to "Link2Butt"
BigButts block on the left - remove "From a template..." button
rename "Auto Launch" to "Launch on Startup" (assuming that's what that means, that Button2Key activates when Daisy's App launches)
remove "a device shows its x ButtScreen."
Move the "Save" button to the right of the Passphrase box and resize that box accordingly so all save is on the same line but the block stays the same width
Rename "Devices" to "Remote Devices"
Rename "Button2Key Remote on of off" to "Status" with "ON/OFF" button to the left of that and the "slick to set a short" rather than being a text field is a button that looks like a joystick icon that opens a small window to get the binding that can be both keyboard or joystick/button box input. Then put Auto Launch and Titlebar Status on the same line (with the checkboxes)
New Profile: When adding a ButtScreen in the list of devices add the Dragon Touch X10 (and the appropriate settings).

publish as next sub version when ready

Module: Audio - Submodule: Volume
Add a new panel just above "Apps" that would show Bluetooth devices that I've selected, so in the top right of that block we'll need a gear icon to show all Bluetooth devices where I can select the ones I want shown - then those should show their connection state by color (blue connected, white not) with a connect/disconnect button next to the device

Module: Gaming - Submodule: Button2Key
Bug - I'm not able to change the active profile at-will, right now it is not enabled and is on the Default profile as no game is running but I should be able to select any profile at any time, it's just that the running process switches to that profile if it sees that process running but don't lock to that profile if the user changes, it just does the switch 1 time the first time it senses the process - that only resets if the process stops or the user manually disabled Button2Key. We also don't need to force a profile as when nothing is active it doesn't really matter what's selected (unless I'm wrong)

Module: Main App
Issue: Let's make a note that as we create new modules we don't need to write out big descriptions in the app, I know what it does, I'm asking for it and if I'm confused I'll get clarity after you code

Module: Claude
New Feature: In the search bar I'd like a button that simply shows everything I've typed in not only this session but for the entire project. That should open a new window with the session at the top and everything at the bottom and it should be an appropriately sized window. It too should have a ctrl-F search box that can do word count (and anything else that's normally with that feature if others). That way I could manually review everything I've "said"

Module: Claude
New feature: When you need to prompt me with a question please sound some kind of notification sound

Module: Claude
Change the following button values in the "Session" block
"Export..." to "Export"
"Copy session ID" to "(COPYICON) Sess ID" (where COPYICON is just that, an icon that's appropriate)
"End Process" to "(PROCESSICON) Process" (where PROCESSICON is some kind of red x with the entire button outline and text being in red

Module: Claude
The "Activity" block, the legened below start a new line where each of the colored dots are so that each line starts with a dot then change "Waiting for Claude" to "Waiting" and "The web" to "Web Access" and "Other Tools" to "Other Tools"

Update to 2.9 and publish

## 2026-10-22

New Submodule - Tools: Notepad
Description: A share notepad space, so one of the DA's will act as the server and the rest clients, since we've built that into Claude I don't want to use a bunch of different ports for different functions, so this should use the same ports that does. It's a basic notepad with basic formatting, basically the standard windows notepad. Anything typed in any client updates all clients, if changes are coming from another client give it a color to denote this sorta like google docs does, though this app is MUCH more basic, just for keeping ideas between machines.

Text Changes
Module: Hardware
Submodule: Performance
Remove text: "Apps (programs with a.......)"
Remove text: "None yet. Click a......."

Module: Video
Submodule: Mini Mirror
remove text: "no mirrors yet. Press......"
remove text: "How it works" - Header should be "Current Mini Mirrors"
remove text: "1. Press New mirror, or the......" 

Module: Gaming
Submodule: Button2Key
remove text: "nothing is sent while....."
remove text: "the profile selected below....."
remove text: "none yet: this profile......"
remove text: "each game's program name......."
change text: "No controllers are plugged...." to "No controllers detected"

Submodule: ForzaWheel
remove text: "Mutes the real devices for....."
remove text: "Nothing muted: Forza sees the......"
remove text: "while one of these programs......"
remove text: "the game's forces, passed from the......"
remove text: "the pedals' and wheel's last few seconds......"
remove text: "the bindings are kept in ForzaWheel.json in......."

Module: Settings
Submodule: Audio
there are 5 setting blocks - let's place them in a 2x2 grid with Signal Options removed since there's nothing in it

Submodule: Video
make it a 2x2 grid of the 4 boxes

Submodule: Overlay
Make it a 1x1 side by side for the first 2 with "Games" being full width

Submodule: Network
Put "Hosts" on the row with Use Zabbix (to the right of it) with Server below

Submodule: Claude
Each of the first 6 blocks have a huge block of text right under the header or option that explains that block, remove it fully, I know what these tools do, I built them with you and these are for me.

Update to 3.0 and publish

```
Module: Tools
Submodule: Notepad
Change: I don't want the text to have a colored background, ever. Let's remove the color all together
```
```
New feature: You can pop out the Notepad window so that it will show as a separate item in the Alt-Tab list. If it's popped out when the app closes it should open popped out again, all other functions are great.
```
```
Module: Video
submodule: Resizer
remove text: "Checks for newly......"
remove text: "The apply button....."
remove text: "Also apply-group......"
```
```
Module: Video
Submodule: Recording
remove text: "Measures the game in front....."
remove text: "Encoded by the graphics card....."
```
```
Update to v 3.0.1 and publish when ready
```

```
Module: Claude
In the Activity window let's make the legend at the bottom 2 columns with 3 each.

Also when I paste from the Notepad app it's pasting in rich text it seems as I see a background color, etc. - nope, I don't want rich formatting just simple text so if I copy/paste it's just text with no formatting

publish as 3.0.2 when ready.
```

```
Module: Butt2Key Remote Android Client
Bug: when typing in the passphrase for the first time it is visible, if I then tick the box, then untick it starts to hide it but it starts unhiden without the "show" box being clicked.

The "X" to close the app should disappear a few seconds after you touch the screen or start a ButtScreen. Then if you press and hold for 3 seconds that button will show up and you can click it, after 5 seconds it fades away again (always fades in/out)

Publish 1.1.4 when ready
```

```
Module: Video
Submodule: Resizer
Remove entire block "Ways to apply a profile"

Udpate to 3.0.3 and publish when done
```

```
Module: Main app
New feature: A button with a notepad icon in the upper right, to the right of "Update" that pops out the Notepad app (if not popped out, if it is and I click that just switch to it). Also change "Update" to just the icon for the button rather than the word

Update to 3.0.5 and publish
```

```
This is a question only - do not code - I want to add the telemetry you helped me code that we added to Forza Horizon 1 to this project that added FFB/wheel support. Also we need to verify that this project let's you use separate wheel/pedal USB devices like my setup.

The FH2 project it: [iamAzoDx/fh2wheel: Play Forza Horizon 2 in the Azimuth emulator with a PC racing wheel and real force feedback.](https://github.com/iamAzoDx/fh2wheel)

And this is my fork of PinyonShift for FH1 that has the code you created in it: [rosscarlson/pinyon-shift: A playable preview of Forza Horizon static recompilation](https://github.com/rosscarlson/pinyon-shift)

Again I just want a BRIEF answer of can you add telemetry for Simhub AND separate wheel/pedal/handbrake/shifter devices
```

```
what would the reverse engineering take? agian don't code and be brief, what are the chances of success?
```
