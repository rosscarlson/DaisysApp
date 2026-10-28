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

```
so would you give me an app/script to run while I play the game that creates logs for you to analyze? WOuld you know a lot more if we did at least one test to see what data you found?

Would I just setup the game with Azimuth/This tool and get ti playing - then run some script you give me? Again no coding and BE FUCKING BRIEF
```

```
Give me a quick brief so i can start a new session with a new working directory
```

```
Module: Tools
Submodule: Notepad
I'd like one visual feature if possible - if I type three dashes, "---" it will create a line across the page to separate it from the other sections visually. If you can't do that without adding a bunch of other rich text stuff a line of just dashes is fine but I don't want it to wrap so it should only be the width of the page, or if that's again too hard say 30 characters long

Issue: Would it be possible to set THIS version of Notepad as my system default for .txt and other files? So when opening them rather than the standard Windows Notepad opens this tool, popped out in it's own window, would be what would open them? I'd want a button in system to set this as the default for a list of appropriate file extensions (you suggest them) and a button to return to before we did that (so record the system state, not just stock state).

New Feature: Add a tabbed inteface where I can create new tabs, close them, etc. - no saving or anything, just multiple sessions and all those sessions sync between all instances just like the main one. You can create new sessions on any client

Update to 3.0.6 and publish when complete
```

```
Module: Claude
When starting a new session the input/chat box at the bottom is geting pushed off the screen on my laptop so we need a scroll bar for that left section. Under "Recently Closed" rather than a list like that let's make it a drop down so it uses FAR less space, just for that section

Issue: In the "working" module you show 305s rather than 5:05, so once we go beyond 60s anywhere let's start showing in minutes, lets also not show "s" anywhere and do the same, going to minutes above 60 (no . is necessary either, just 60 or 1:01)

Issue: there's a hugely long tool tip when I put my cursor on the chat input box, remove it fully

Block: Session
Why do we have a separate "Don't Ask" in the drop down and checkbox for Allow Everything? I'd rather than just be in the dropdown if that really is a separate setting, also the X Process button, isn't that the same as the "Stop" button that's above the send button? Or is it to kill the Claude Code process? Either way - remove it.

Block: Usage
I don't really understand "# turns - about $1.26 at API prices" - I assume it means what this session would have cost at API pricing, is that right? Let's hide it with a setting to hide/unhide it, that entire row of text but leave the tokens this session above.

Block: Thinking (the top right block)
Issue: When it shows "Done" or a single word like that let's make sure it's centered from top to bottom - so right now it should be inline with the big green image but it's not, it's one "line" above it.

New Session Screen Changes
- Change "Which AI?" to "Host"
- Change the drop downs to "Local" for local and "GAMER (Remote)" for a remote session (with the appropriate name of course)
- Move "Where should it work?" to just below the session name, etc. and change to "Working Directory" with the existing check boxes for the various files to open below it.
- Remove text "Read first: tick the project's........"
- Put the boxes for Recently Closed and Previous Sessions side by side with the open feature part of the Previous sessions
- Remove text "Or open one by its id......."
- Remove text "Then write below what......."
- When selecting a remote machine and the block "Its sessions" shows that should show above Recently closed.
- Change "Recently closed" to "Recently Closed", "Its sessions" to "Remote Sessions", "Or start a new one there" to "Remote Session Details"

publish as 3.1 when complete
```

```
Module: Claude
New Feature: Ability to interrupt the current running process to give it more/relevant information to it's current task, so it would basically pause, take this input, then continue with this new information - if this feature is possible with Claude Code Pro. If so let's add a button above Pause called "Context" that would open a small popup window to let me type in the new information, in a small but still multi-line box.

Module: Gaming
Submodule: iRacing
I'd like a button at the top, between Sync and Import that would generate a webpage/html of this entire page, formatted as close as possible, that I can then share with someone. Nothing live, just a static page as an export (with the button called "Export"). Also rename "Import..." to "Import", and "Settings..." to "Settings"

Module: Network
Submodule: Zabbix
- Devices that are in alert status should be shown first, with critical, the urgent, etc. in order of severity. This is a separate, new "group" so any devices that show here will still show in their normal group slots as well so there are effecitvely copies/duplicates of those entries
- When clicking on a device let's add a "Hide Device" button in the upper right with appropriate icon on it that does the same as right click-hide
- Let's group the devices together by, well group - but visually. So each of our current rounded corner boxes would be contained in another outer rounded corner box for that group, the header would show the group name with an icon showing the status of all the devices in the group, so if all good it would have a 3 for example (in a green circle) but if 1 was ciritcal and 2 good in that same group it would show 1 in a red circle with 2 in a green one.

Publis as 3.1.1 when ready

Please continue where you left off.
```

```
Module: Gaming
Submodule: Button2Key
Bug: I currently have 3 processes setup for a profile, Forza Horizon. B2K was "Off" but the FH profile selected, the game launched yet B2K never enabled. I think we also have confusion around the "default" profile, you say it's required but it should not be, the state of the app should be enabled or not and it gets enabled either manually by the button or hotkey (we need a hotkey button binding next to the ON/OFF like in ForzaWheel (same icon please) or when it detects a running process. If started by process when that process ends B2K should disable. Then at any time I can change to any profile I want manually, starting/stopping. You only start if you detect a process start or stop.

Publish as 3.1.2 when ready
```

```
Module: Main app
Issue: When something has a notification in the title bar in green reflect that color in the system tray icon, changing the white flower to green.

Module: Tools
Submodule: Notepad
Let's not auto-name the tabs, make them Notes 1, Notes 2 - but if I right click I can change the name (it changes everywhere)

Publish as 3.1.3 when ready
```

```
Module: Tools
Submodule: Notepad
I didn't mean right clicking on a tab would open a new window to rename it, I want it to work like the tabs do in the main interface, right clicking just let's you edit the name of the tab right in place, no popup opens.

Publish as 3.1.4 when ready
```

```
Module: Tools
Submodule: Notepad
New feature: Ctrl+Z is undo, I want Ctrl+Shift+Z to be redo with a history just like undo. so if I undo 4 times and don't do anything I can redo those 4 things, once I do something (type something) that's broken and it starts over

Module: Main App
Rather than making the white Daisy green let's leave it as white but change the background to green, so the background should match the app titlebar color, if there's ever red it goes red otherwise blue is nothing and green is, well green.

Release as 3.1.5 when ready
```

```
Module: Gaming
Submodule: Button2Key
Was playing a game with an associated process. I stopped playing the game but Button2Key stayed active, it should stop when a watched process stops regardless of how that process got started.

Update to latest subversion and publish when ready
```

```
Module: Tools
New submodule: Terminal
A multi-session termal that allows me to choose between a CMD, Powershell or WSL terminal (with the default type when creating a new session set in settings). I want these termainsl to use the system default colors/etc so they are basicall those wrapped in our app. You can create new session just like the Claude module so a tabbed interface where you can right click to rename, etc.

Publish with new subverion when done
```

```
Question: Right now the Claude Code module connects to various Claude Pro accounts to access the local Claude Code app. Would it be possible to make this more generic - so our front end works the same regardless of the LLM/backend?

So we'd have our own client that has basically full control over the PC, including controling the UI just like Claude Code does. That way again our UI currently stays the same but as you start a new session you could choose which backend you wanted, starting with Google Gemini first. Perhaps each LLM/AI add has it's own set of parameters, etc. so that we can get the same basic funtionality out of them - and that is to create code for windows, Android, python, etc.

Is that possible? What won't work how I want? Again I want to start a session with Claude Code and ask it to code me an module for this app - or to start one using Google Gemini and have it do the same, write modules for this and other apps. I'd also want to be able to use Ollama with local models running on my local hardware, though my 16GB VRAM limit may, well, limit that.

No coding, just answer and be brief.
```

```
"wrapping Gemini CLI would get it close."

Does that mean the Gemini CLI would have access to things like web, subagents, planning, etc. etc. like Claude does, perhaps just not at "good"? Or do we still have those massive limitations?
```

```
Module: Gaming
Submodule: ForzaWheel
Remove the "Using it with Forza" block in the bottom left

Module: Network
Submodule: Zabbix
You misunderstood my request to group devices, I like that they are grouped but the groups should only be as big as how many devices are in them so groups can be beside groups in the grid layout we had before, in fact the "Display Gridsize" should be in settings with 2 fields in a new block to the right of "Server". The "Alerts" block should go first but the rest can then be dragged/dropped to move them around, and if it's 4 items and a row has 2 columns left it should take those 2 and then continue on the next row so that there isn't a lot of empty space like there is now

Module: Tools
Submodule: File Sync
Issue: The "Recent Syncs" block (please change the titel to that case) and the pop open box when you're running a sync have windows that show error logs, both should be able to be copy/pasted and those errors should be logged to the app's logging feature as errors.
Change text: "What a sync would do" to "Sync Details"
Feature: When a compare is running you show me how many folders are being found, please also list how many files are being read

Publish as 3.1.2 when ready
```

```
are you done?
```

```
yes
```

## 2026-10-23

New Module: SimEffects
Description: Will be a replacement for the huge tool Simhub. We'll replicate the features as I need them, the first will be the "Bass Shaker" feature where the telemetry from the game causes various audio devices with connected transducers to shake my sim rig. it needs to support multiple audio devices with per-device volume level as well as per-effect volume level. When adding a device we should prompt for what type it is, including common ones (like Dayton Audio, ButtKicker, etc) so that when sending sound to the transducer we send the most appropriate frequences/etc. for that physical device

The main interface would allow for the addition of multiple effects where each has it's own level and appropriate settings. We'll need profiles for various games with the UDP port being set per-profile vs. for the overall module as some games use different ports than others. At start I want to support the Forza Horizon series, Forza Motorsport, iRacing and Assetto Corsa Competitizone. I'd like default profiles for each game with the best telemetry items for road feel, impacts, crashes, curbs, engine vibration and any others you feel are appropriate. I'd like level meters in the approrpatei places for visually monitoring what's going on with a old school audio spectruam analyzer style of top/bottom bars in a line, showing each effect as it triggers, this would live at the top of the list of all the effects in the order of the effects below it and those effects can be reordered by dragging/dropping. Each one is expandable/colapsable so you can open it for it's specific settings then colapse to save space but in the colapsed state you can still see/interact with the volume slider/mute button. That volume up/down/mute should have a small popup window to bind hotkeys/buttons for it. Same goes with basically anywhere the is a volume slider, and there should be an overall slider at the top right of the main window of this submodule.

When ready publish as 3.2

Issue: I dont' see the Dayton Audio BST-2, can you add taht to the transducer list?
Issue: In each effect for "Plays On" just show the device name (so Buttkicker) not with the description after so the buttons are smaller.
Issue: We don't need the word "On" "Off" to the right of the green button that says the same thing
Issue: Let's move the "Profile" and it's dropdown/new button to the top of the Profile block over on the left

New Feature: To the right of the "On/Off" green button give me a button with the joystick icon to bind a hotkey for it

New Feature: on each specific effect I need to be able to adjust how much is sent to each device, so each device - if enabled - needs an output/level slider. Let's also move the Test/Remove buttons to the bottom right

Publish as next subversion when ready

When starting a new session we need to be able to select the Claude Account we want to use for that session so if we start another right after it can easily use a different Cladue account rather than needing to use the switcher on the bottom right. In fact to keep things from getting confused the right bar should really not be shown when creating a new session so let's just hide it entirely

Remote clients should also be able to select the account, so we just need a new dropdown between Host and Permissions that allows for this. If we select a remote host it should return what accounts are available on it to select from, the mobile app I'm building will need that too.

When ready publish as 3.2.1

Bug: When changing accounts in the bottom right corner that should only change for the active session, not ALL sessions, so the right bar should be contextual to what's happening to the left. Let's also add a refresh icon to the left of "Usage" on the button.

Bug: There's no audio output - I made sure to remove all audio devices from ShakeIt Bass Shakers in Simhub (which is sitll running) but made sure the we're getting the data, and we are. I see the levels in each appropraite channel but there's no vibration - I did add my main speakers as an output but I hear a mid-high frequency pitch (higher than middle C but not sure where) so perhaps it's sending the wrong frequencies?

publish as next subversion when complete

Module: SimEffects
Submodule: Bass Shaker
First, rename to just "Shaker"
New feature: A Level Wizard. It opens a new window to take you step by step through each of the effects allowing you to adjust it's level via hotkeys while you drive. The first step is to create/confirm those hotkeys, they'll need previous/next step and level up/down as required then if they want frequency up/down, sensitivity up/down, threshold up/down and length up/down. As you move through the wizard if those are not bound denote that in the interface but still allow for manual sliding of them during the wizard. There's also a binding for all/solo so that you can tune just that effect but then turn everything on (as it was before the wizard started with the changes that have been made along the way) so you can isoalte the effect and feel it in context.

Release as 3.3 when ready

Module: Gaming
Submodile: Butt2Key
Bug: Process watching is only working if I switch to the module, when I do and a game is running bam, it starts right away but not until I click on the module

The audio level issue is this - the sound device levels were at 0 for some reason, in the UI we show a volume level next to the device but that didn't change the device in real-time like the Audio-Level module does so they were all at 0 volume but show at 80 in the app.

Publish as 3.3.1 when ready

Seperate effects by all 4 wheels so we can do front/rear, side/side, all 4 wheel setups. So an audio device can be left/right and that can be assigned to front/back - or a device could be front and it's left/right be left right for the frront, etc.

When ready update subversion and publish

Module: Tools
Submodule: Notepad

I see no way to connect to a remote device - in the drop down when I choose "This PC" at the bottom it should say "Add Server" if it's not connected so I can do that with the passphrase

Publish as 3.3.3 when done

## 2026-10-24

MOdule: SimEffects
Submodule: Shaker
Issue: Every place you can bind a hotkey it should also be able to be a keyboard key
Issue: While you're using the wizard to set levels those levels should be reflected in the main UI while you change them so you can see them change on another display while using the hotkeys
Issue: While in the wizard the effect you're adjusting is lightly highlighted - let's make this more obvious by making it bold and making the level meter "brighter" (as possible, don't recreate things)

Publish as 3.3.4 when ready

Module: Claude
New Feature: Currently when starting a new session we can select any of the .md files in the root directory of the project - the issue is for this specific project in src/Modules/(MODULE)/README.md all exist for the different modules and I'd like to be able to select the appropriate one when starting a session to better streamline what the AI needs to know to keep everything efficient

So I think when we browse to a directory we need to get all the sub README.md files and show them with the names of their modules so we know what to select. Then let's use some kind of multi-line select box that's fixed at 4 lines, with scroll bars, with each item getting a check box. We'll make a similar list using whatever is found in the root directory, so what we have now but moved into that box, with it coming first and labled "Project Docs" and the other as "Module Docs", is no sub modules found don't display that box.

Publish as 3.4 when ready

Many of our modules have the ability to bind a hotkey to a function, that should be either a keyboard shortcut or button on a joystick device (wheels, throttles, joysticks, etc) - does each module have it's own code for this so we have a lot of redundancy, which is why it's a little different between modules? If that's true write a brief so that we can migrate to a single hotkey system, managed in settings, for all hotkeys. So there is a base way that a bind button is called/used so that we don't rewrite this every time we add this feature to a module.

The settings tool would simply list all existing hotkeys that have been created, with the ability to edit it (change what key is bound), delete it, etc. Speaking of - when we need confirmation in many places if you click "Delete" it changes to "Confirm?" and waits for input before changing back. Similarily I want to handle that with a shared bit of code so that any time we have something that needs confirmation it's done that way without reinventing the wheel every time.

So don't code - but analyze and write me a brief and also let me know if this is something we should address, I worry as the project gets bigger and bigger

how about you just got ahead and do both - when you're done publish as 3.5

Please continue where you left off.

Module: Claude
Issue: I saw you working on the new "Module Docs" section so that when I start a project I can select the submodule I want you to work on. I don't see that now, so when you load a folder and get the *.md files you need to search all subfolders, then use the folder name as the module name (for the README.md so I know which is which). I don't see this in the UI, it should be to the right of the "Project Docs" that I do see. Then Remote Sessions and Recently Closed should be on the same "row", side by side rather than above/below each other. Let's put "Recently Closed" on that same "row" and make it the same kind of UI, with each block having a maxiumum of 3 "lines" of entries to keep the box from getting too big.

Module: SimEffects
Submodule: Shaker
Bug: Default UDP port should be 8000 for FH

New Feature: We have a widget system for the Hardware module, Performance submodule. I'd like to be able to pop out the entire section of bar graphs that show the various outputs on each effect. However I'd like to make sure that we are reusing the same code from the Performance submodule rather than reinventing it each time.

We use these same widgets in other places in the app too, notably in Gaming > ForzaWheel. Again let's make sure we're using the same code/functions for each to keep from duplicating code that'll be hard to support. Please note in the main README (or where appropriate) that if we create a "widget" again we reuse the same code for it.

Publish as 3.5.1 when ready

Module: Claude
Issue: When using a remote session the "Claude Account" box in the bottom right isn't shown and I'd like to be able to change the remote client's account just as if I was using that client.

Module: SimEffects
New Submodule: Arduino

This new module, Arduino, will let the user specify what features they have setup with an Arduino, the COMM port it's connected to. Then based on what's enabled additional submodules will be enabled, with the first being "Notify". In Notify we'll have 2 main light types, "Notify" which are 8x8 RGB grid lights for gear number (and flash for over-rev) with the other being "Shift" which is a series of 8 or 16 RGB LED lights for a shift indictor. We'll need settings for redline, etc. etc. but I want it to figure out where the redline is based on the car (like Simhub does now). Finally we'll also want a submodule "Fanatec" that lets it control the shift lights on my Fanatec Clubsport RS wheel.

There will be other features that the Arduino will support so build this in such a way that "plugins" can be built for additional features that the Arduino would be powering (motor controllers, fan speed controller, etc - don't add these now, we'll do that later). The tool should be able to handle more than 1 Ardunio so we'll need a way to manage them separately, this should all be in a popup tool similar to our other wizards (but not really wizard).

Publish as next subversion when ready

(Asked in the Daisy's App Mobile session, D:\OneDrive\Code\Android\DaisysApp:)

I want it to do that in a consistent way so that all remote clients can work the same by specifying a Claude account to use at session start time or to change that in real-time for a session, I don't see that in the Android app. - the new feature we added to browse all the submodule isn't showing up in the Android app - I'm guessing it's pulling a different way? In our API for remote access to Claude - and remember I want the app to have generic remote access, like we use for the notepad/whiteboard app - such that we have a set of shared networking code for our API (and we need to create API.md in the root folder to document the API) so that all this is consistent across features and interfaces. So when a remote client loads/starts a new session the working folder should return both the *.md in the root but also the "sub feature" readme files, if they exist - and we should be reusing that code when necessary. In the Android app add the "Module Docs" as a block to the right of the existing "Read First" (and rename that to "Project Docs" so we're consistent). So each takes half the screen with the message box at the bottom like it is now. Also when in a specific session in the CC Remote module the menu in the top right allows for "Reconnect" - let's move that to a button just to the left of the Usage/stas button with a "refresh" style icon.

In our main project, Daisy's App (D:\OneDrive\Code\Windows\DaisysApp) we have an Android app that links to Butt2Key Remote - I'd like that exact same code/client integrated into Daisy's App Mobile (this app) but do NOT recode things - link to that code somehow such that if that code gets updated it'll update this app when you push a new build. We should note in the readme in D:\OneDrive\Code\Windows\DaisysApp\src\Modules\Butt2KeyRemote so that when we work on that module if we change the Android app we need to update both it AND Daisy's App Mobile since they share code.

Publish as next subversion when ready

Module: Claude
Issue: When using a remote session the "Claude Account" box in the bottom right isn't shown and I'd like to be able to change the remote client's account just as if I was using that client.

New Feature: Ctrl+W should close a "tab"/session with Ctrl+T creating a new one and Ctrl+Shift+T would reopen the most recently closed tab, just like modern browsers work.

Release as 3.5.5 when ready

Module: SimEffects

Issue:
When adding an Arduino I'd like some kind of status light to show we've actually connected as when I hit Test on a feature I get absolutely nothing, also the data pin box should be a dropdown, not an input box.

The "Remove" button should be moved to the right of "Add Arduino" with both the same size and "Remove" only being active if you've selected a device.

I see something about it trying to connect, "is the DaisySim sketch on it"? - do we have to change the arduino, I'm using these with SimHub and that works fine so shouldn't we be able to talk to it fine so long as we know what's on what pin? Is that why it's not connecting?

Release a next subversion when ready

we have a major bug - the code that we updated recently for button/hotkey bindings is somehow sending keypress/etc as suddenly Forza Horizon is lossing FFB. this happens if you're driving on a wheel and you type a keyboard key suddenly all input goes to the keyboard and you lose FFB - that is happening whenever Daisy's App is open now and gibven the changes we made I bet that's why. I had to roll back to 3.4 to be able to play again (not positive which version the bug showed up in but it was post 3.4)
Can you track down this bug and publish as 3.6

go ahead and release so I can test with it and I'll let you know - it was very suspicious that the bug existing after we changed our hotkey/binding code - if you're sure the app isn't sending keystrokes with Butt2Key off then fine - as that seemed to be the bug.

Release when ready

publish your release now/first, then you can deal with the following (I stopped you)

So again publish whatever your current code is NOW then work on below

go ahead and release so I can test with it and I'll let you know - it was very suspicious that the bug existing after we changed our hotkey/binding code - if you're sure the app isn't sending keystrokes with Butt2Key off then fine - as that seemed to be the bug.
Release when ready

## 2026-10-25

Module: SimEffects
Submodule: Shaker

I want to be able to right click on the "level" section at the top with an option "Make Widget" - then create a resizable widget that can be set on top, etc. - we've done widgets in a few different modules so as another task we need to consolddate this code into the API so that anything we make a "widget" uses that code and features so they are all consistent and code is efficeient.

Module: SimEffects
Submodule: Shaker
Issue: When clicking "test" it seems that test is not at 100% power, let's make that variable a setting in the settings so it can be changed

Module: Claude
Issue: When selecting "Module Docs" please sort that box by title

Module: Video
Submodule: Recording
Issue: If Virtual Webcam is running and the app restarts virtual webcam should restart - also in that event or if you start the virtal webcam from the shortcut key it should default to None for audio and stream main system audio (vs comms audio, do NOT use that)

Module: Claude
New feature
While Claude is coding the header should show notification - so it would say "Coding" and use the appropriate background color to match the colors in the app. Also I want to make sure we're using shared code API here, that each module doesn't have it's own code for modifying the header but rather is calling a shared function that handles the header with a single set of code.

When ready publish as 3.7

continue

Question: I have a Fire TV Stick 4K that I believe is "hackable" with ADB to "debloat" it - if that's possible and smoething you can do/help with please continue, if not stop now and do not process the instructions below.

Module: Video
New Submodule: FireTV
Goal: Build a tool that would debloat an Amazon Fire TV stick (list models that are appropriate) then given me whatever options you can come up with for removing things like ads, apps, etc. There are only 3 apps I care about, Plex, F1TV and YoutubeTV - everything else can be removed, as possible.

I'd love this to be a tool friends could use to do the same - if you can scan the network to find devices great, if not give me a field to put in the IP address of the device.

Publish as 3.8 when ready

I scan, it finds my device - says "TV - 192.168.1.82 - ADB on" (that is the correct IP) - it then says "On the TV: tick Always allow from this computer, this OK." - but I see nothing on the TV and it goes no further.

If a bug fix - if I'm missing something I need to do on the TV tell me here and I'll tell you what message to give to users on new connections.

Release as 3.8.1 when ready

I see "Remove Ticked" - I'd rather it "apply" what is ticked, so if I untick something that is me turning it of - and if it's ticked it gets turned on - so I think backwards from what you had.

Let's also update the error - I had to disable/enable ADB access then I got the prompts - so if we hit that error we should pop open a little wizard that walks them through each step (settings, my fire tv, etc) with next and quit buttons with the final one making the connection again to get the prompt on the device.

Update to 3.8.2 when ready

Give me more insteresting output in the "Done" box and rename to "Status"  - it' just simple text but format it it better, show what you're doing and the reply in a "prettier" easier to read way.

After you "apply" at the end of that process if we can we should reboot the device for the changes to take effect.

Let's move "Installed app" to just above "System updates", so the order should be "Amazon", "Ads/Tracking", "Installed Apps", "System Updates" (with that case)

Bug: The "Ad-blocking DNS" change completely breaks the TV, it acts like it doesn't have internet access at all so we need to fix or remove that if we can't make it work. My own internal DNS uses PiHole so I'm blocking as much DNS there as I can safely.

Update to 3.8.3 when ready

You can use my live TV at 192.168.1.82 as you want to test with it

When the Fire TV stick restarts I see in the log that it's turning the things I turned off back on - so it's not saving somehow. Can we fix this, if so do if not let me know.

Publish 3.8.4 when ready

okay - so it worked and removed a few of the apps but I still see "Tubi" "Music Videos" "Kids+" "Luna" "Amazong Live Shopping" " Picutres and "Xbox" none of which show in the app. I also still see all the ads at the top - I see you removed the DNS setting but the way they are pulling the ads is getting around Pihole. So at the end of the day this tool does next to nothing as it's only removing a few apps (I dare not touch the system apps you have hidden) and it's not killing ads - if this is the best we can do great but it seems from what I've seen online that we should be able to do more.

If you have code updates publish as 3.8.5

Remove everything we just did as we didn't really accomplish anything. Just roll the code back to what I think was the last version of 3.7.x as I think the first 3.8 was our first version of the Fire TV module.

Module: Claude
Issue: Let's move the "Claude Account" dropdown and refresh button to the bottom of the Usage block rather than have it's own block

Module: Claude
Issue: Scrolling back in time at the top I see "387 earlier messages aren't shown" - give me a button to show them by backfilling the list so I can continue to scroll backwards all the way to the beginning of a session.

Module: SimEffects
Submodule: Shaker
Issue: I created a widget from the meters then deleted it, tried to create it again and I get "specified element is already the logical child of another element....."
Feature: When a widget has been created for the app (in case we add more) a new block should show in the bottom left "Widgets" so you can edit/delete those widgets

Module: Tools
Submodules: Notepad
While Ctrl+T opens a new tab CTRL+W doesn't close it, add that

Module: Gaming
Submodule: ForzaWheel
When the input overlay is enabled it should only show if ForzaWheel is enabled/the game is running

Module: AI
Issue: The "Module Docs" window is good but still hard to read - so for each entry show me a list like this, if there is a submodule it should show the main module, sorted by that. So

Audio -> Volume
Audio -> Levels
Audio -> Delay
Video -> Resizer
Video -> Mini Mirror
Video -> Overlay
Video -> Recording
........
SimEffects
AI

SO they aren't sorted alpha - they are sorted the way they show in the interface so when working on a module I know which one to choose and which module it's a submodule of

Publish as next subversion when ready

Module: SimEffects
New Submodule: AmbiLight
Details: I have a set of Govee Neon Rope Light mounted behind my monitor, in Simhub I can connect to that and have what's on my display control it much like "amiblight" from Philips. In the app you'd be able to specify, or it would know, how many segments the lights have then give me a virtual display (using all my displays since I have triples, etc) to let me place and size each of those segments such that whatever colors are behind that segment get replicated on the actual lights, if we can control all the way down to the individual LED level great - if there are 100 break it into 10, 10 light segments (this should be an option somewhere).

The concept is to replicate what's happening on screen in the lights on the wall.

Publish as 3.9 when ready

## 2026-10-25

Module: AI
New Feature: On the "Search" bar I'd like a icon/button to the right of the current speach looking one that opens a small window to show all my prompts, not including everything that's sent, etc. - just my prompts, in the style of a chat session so it's easy to see each of my prompts with ONLY your final response. So it's easy to see how the conversation/work flowed.

Module: AI
Bug: In the Usage box if you change accnounts while a session is paused due to over limit it should NOT auto-pick back up, it should only update the usage stats for that account. Then the user could continue if they want but it should just auto-continue.

Now if a session is going and it hits the limit and waits it should auto-restart, it's just that it shouldn't when I cahnge in that box. If I do change then I resume a session it does use what's showing.

update to 3.9.1 when ready

Module: SimEffects
Submodule: AmbiLight
When adding lights we need some kind of "Test" button that lights up that light say all red for 2 seconds. Do not change that light's state or if you do put it back like it was before or if you can't do that turn it back off. Before a light is added we need to select the monitors that will be used as I have 3 across the top and 2 down below and only want it on the ones above, so each monitor should have a check box on it so you can turn that monitor on/off (put that in the center of the monitor please). Then if you click on a segment that segment should light up red while you're clicking on it/moving it so you know what you're working with. FInally you have names like "H60A1" "H6641" as the lights, if you can put friendly names for GoveeLights that would be great as right now that's all I'm supporting.

Release as next subversion when ready

Module: Gaming
New Submodule: Calibration
I'd like to recreate the funcationality of DIView calibration tool from Heusinkveld that allows for min/max/center calibration for joystick devices with axis (pedals, wheels, sticks, etc) - I use it primarifly for my SimJack Pro pedals but other devices should be usable too.

If you can recreate this please do, you'd select the device to calibrate from a block on the left with the right showing the various axis you can calibrate, showing the raw data as well as calibrated data. You then click on an axis to open a small window that lets you set the min/middle/max settings with a little graph showing you what you're getting so you can dial it in. That window should have "Save" and "Close" buttons. This then writes to the OS the same way DI View (or oather calibration tools) do. Finally there should be a per-device wizard, so it would guide you through each axis, with skip/ignore buttons for those you don't need to change, with the app detecting the min/max levels then calculating the middle point for a straight line. Show visual indicators for the user and a reset button for each step if they want.

Publish as 3.10 when ready

Module: AI
Issue: When creating a new module the "Project Docs" and "Module Docs" are roughly 4 rows tall/long, make that 6. Also rename "AI to "Claude" in the "Module Docs" list

Module: SimEffects
Submodule: AmbiLight
Issue: We need the joystick icon/button to bind a start/stop button for the On/Off here. Make sure you reuse our existing API and don't rebuild it. Also we need to show in the header as a green bubble when running ("AmbiLight" in green is fine) again use the existing API we have and don't rebuild this.

Module: Gaming
Submodule: ForzaWheel
Feature: The "input overlay" - I'd like to add/change a few things, can we vary the speed of the graph so the left 20% of it runs at 2x the speed of the rest with a smooth change between the two speeds. That way viewing in real time is basically the same but the graph can store a lot more history in the same space. Also above the brake/throttle/hanbrake (add that in orange color) put the percentage it's being used (as a number only, no need for "%")

publish as next subversion

Module: Gaming
Submodule: Calibration
Issue: During the wizard you should be able to click on the number of the axis in the list above to jump directly to it - and if that axis is detecting input it should show that by turning blue (and be green when selected)

Bug: My Simjack Pedals, only the Z axis is working but "X Rotation", which I see fine in the joystick control panel and is showing me fine as I move the pedal.

Module: SimEffects
Submodule: Shaker
Issue: The "Crashes" effect doesn't produce the "thud" I want - and when testing it I get a weird thud, thud, thud that's pretty light - I want it to replcate what I should feel from the game, and this is doing very little. This should be one of the strongest sensations, along with impacts next and curbs after that but none have the big hit that I know the hardware can deliver.

Module: Gaming
New Submodule: RGB

The ability to control RGB devices via the motherboard, RAM, GPU, etc. - in my case I have an ASUS TUF Gaming Z790-Plus WiFi motherboard with RGB with 9 RGB fans connected, Trident Z Neo RAM and an Asus Tuf 5070 Ti GPU - other machines have similar but random gear, so everything you can add support for is great - that is without needing anything else to be installed, at least nothing that's vendor specific. It should have the ability to apply profiles like solid color, rainbow, breathing, all the standard ones, don't need any user created stuff for now. But ability to set each device to one of those various profiles and a master "OFF" toggle that turns all the lights OFF.

Publish as 3.11 when ready

Module: AI/Claude
New Feature: Add to Usage Block

Just above the dropdown to change profile we're showing the name of the active profile, instead in that place show all profiles, 1 line each with the current status of that account's usage, so green if good and how long until it's good or orange, red based on how much usage, then if you click on the name of the account it expands to show you the 5 hour, daily/weekly usage/limits/times,etc.

Update subversion and publish when ready

## 2026-10-25

New Module: HomeAssistant (use "HA" for the name of the tab)
Description: Connects to an exiting HomeAssistant session to show you all your devices, grouped appropriately with quick toggles on appriproate devices to turn them on/off/etc. with a small popup screen when you click on it (or double click) to get full control over it. The interface should be very similar to Network - Zabbix module with the same type of grouping and box/layout.

All devices that have on/off/amount/level should have the ability to create hotkeys for them using the hotkey API we already have (don't build this new) and devices should be groupable/filterable by location/room (whatever it's called) or by type, etc. When viewing the detail popup window for a device you should be able to hide it from the interface with a section of the settings showing what you have hidden so you can restore them if necessary.

This first draft only needs to give me control over my devices, not full admin over Home Assistant, we'll add more later. On first run/install we need to prompt the user for the HA details (or to just disable the module).

When ready publish as 3.12

## 2026-10-26

There are a ton of devices that are Plex media players, one for each person i've shared with - so a LOT - we need to hide them, so let's hide ALL media players UNLESS they are in an existing group, if they are "no area" they can be hidden.

Release as 3.12.1 when ready

When need the ability to disable Mini Mirror so we need an On/Off green button in the top left with a hotkey binding button next to it - make sure you use our APIs for both and don't recode them from scratch.

Release as next subversion when ready

Dropped a ZIP file with all the logs from another machine in it - Daisy's App is crashing some, not always, there - can you have a look and see why?

Module: Main App API
Bug: No keybinds can be set - existing ones are working but when I try to bind really anything else I get the box ready for input and when I press a button it then says "Click to set shortcut" like it "heard" the input but it never saves it, displays it and it doesn't work.

Publish as next subversion when ready

Still not quite right - now when you press the key you want to use the window instantly closes rather than it showing you what you chose then you click close. Then if I want to bind it agian it should show me what was there before then I have to clear that to rebind it.

Publish as next subversion when ready

just got another crash - new zip file in Downloads - along with fixing that I want a few other changes:

Module: Network
Submodule: Logs
Issue: Move this to be a submodule of Tools
New Feature: Add a "Clear" button that clears the log (with our standard way of confirming that's in the API)
New Feature: When exporting it should only export what's shown, so if the user filters the last 10 minutes or whatever that is waht should export.

publish as next subversion when ready

I want to confirm that if a user turns various modules off in Daisy's App those won't load at all saving code time and possible bugs from those modules, correct?

Whenever an app creates a title notification "bubble" that should be clickable to take you to that specific module/submodule.

Release as 3.13 when ready

WHen you sample the screen are you ONLY sampling the arear I see in the example window as it seems you're sampling an average of a vertical slice in that area - I ONLY want it to sample from the area that I see in the example, if I make it larger it samples larger and it sample is "slices" based on the number of LEDs in that virtual area (since those virtual areas will change based on how many LEDs the device has)

update as next subversion when ready

have you published?


General question - I'm concerned that we're doing things in the AI/Claude module that would be inefficient and waste usage/credits/tokens with my accounts - can you have a look at how we're using/sending the *.md files that we're keeping all our notes in? I want to be sure we're documenting everything as we go but also be careful about how much we have to send to the AI on various prompts - that was my goal in breaking out the README files so each module had its own rather than one huge one, confirm that's a good approach and there's not a better way for us to be doing this efficently. For example I'd not expect you to work on module D when I've old told you to open the README files for module A, B and C (in that example)

Is there are changes that need to happen to code release as 3.14 - otherwise just confirm we're already good

"**One cost that remains by design:** with "Keep session notes" on, every request gets one extra short turn, and that turn sends the whole conversation again. That's what keeps the notes up to date."

Explain the downside of turning this off?

"or ended by a limit"

Are you saying that if we hit a limit and I want to switch to another account to pick right back up disabling this would prevent that from work so smoothly, as it does now? Be brief

"**`.** Your requests stop being logged word for word, so you can't search them later"

Okay, this is reason to keep it ON - what is the downside again in terms of token usage?

can't we just store that stuff locally in case we need it and for search rather than sending it on every request?

yes, and publish

Module: Claud/AI
Block: Usage
Issue: I like the data at the right - 5h 0% - Wk 7% - but let's make them in columns rather than just spaced with a dash so each row lines up nicely. Then we have the 5h usage, Wk usage (change to just W and upper case the h as well) then add a 3rd column that's how long is left until the week resets, in days. So it would be .1D at the lowest or 6.9D at the longest.

Bigger change: Then rather than there being a dropdown box to select the account to use let's just have it change when I click on the name of the account in the list above - it would then go bold and a bit larger font so it's obvious which is active (or some kind of background) with the "Usage" button staying in the bottom right corner so that data can be refreshed, just the dropdown is lost and they are clickable.

Additional Change: On the start new session screen we need to change the layout, rather than in a row let's put Name, Host in one column to the left with Claude account changing and being in the next column/block to the right (with permissions below it) - the change is that rather than a drop down show me the same selection style we have in the Usage block when you're in a session. Don't recode this, reuse what you can so that is linked here basically and if we change it's layout/data that change gets reflected here as I need to know the account details to know which to use.

release as next minor version when ready

you took the word "Refresh" off the refresh button, add it back and release as next subversion

When starting a remote session we don't get the same inteface we just built, we get the old dropdown with the accounts rather than the details - so you may need to add that data to the API so remote clients can get the account details when starting remote sessions.

When ready publish as next subversion

just noticed that the Usage block on the right also has the dropdown interface - if you didn't change that too please do so that it's exactly the same as if you were using the client locally. If you need to add that please do and publish as next subversion when ready.

so i see the accounts but the usage details are missing and there is no refresh button - I'll say again the UI should be EXACTLY the same remote or local - so the backend API needs to handle whatever we need so that, again, all UI elements are the same. So there should be some kind of middle layer that handles all that wether you're local or remote so the UI is the same. So the UI would go through this API regardless of local/remote so that the data is always there so we may need to add things.

When ready publish as next sub version

Module: Tools
Submodule: Logs
New Feature: When clicking on the name of a header I'd like a small menu to let me either sort (alpha/time up/down) and filter by column type (so for Level it would show me each level type, for 'Source' each source type) - for Event, time and message just put alpha/time sorts (nothing for message).

ACtually what is event, as I don't see it ever being used? If we need it great, but if not remove it.

Release as 3.16 when ready

## 2026-10-27

Module: 
AI/Claude

Issues: 
In the "Usage" block remove the text and buttons for installing Claude Code as I'm running a remote session and don't need to see that, ever.

Also in the Usage block right justify the red text for "Back in XH XXmin"

When Claude is active and coding if there is more than 1 session active each active session should show in the header notification area, making sure that if you click on one of them it takes you to the correct tab/process to view it.

When ready publish as next subversion

Module: 
AI/Claude

Issues: 
When renaming a tab in the interface that name should filter into the API and be reflected in any clients that connect.

Publish next subversion when ready

When Button2Key is running it should show the name of the active profile in the header so now it would say "B2K: Cyberpunk 2077", same for SimEffects->Shaker (it would say "Shaker: Forza Horizon" for example)

when ready publish as next subversion

Module: Claude

When you compact a session we should get a status that it's running (right now it just seems to pause) then when complete we need to update the "Usage" to see the new context

Publish as next subversion when ready

if you didn't publish as next subversion do that now

Question - as we work on a project I tend to give you a good deal of feedback at once for you to fix a number of issue. After round one that might be 200k in context - if I then compact the session it drops to say under 20k - I would then give you my next round of feedback for the specific feature we're working on so you still keep the context.

If I had it so that every time you publish a build for me that it auto-compacted after that what would the downside be? Be brief please

okay - great - now let's code this and make it an option in Settings for the Claude/AI module. Remember this needs to work on all clients, local and remote and make it both a system default and an option when creating a new session with the option being on the right column under the Recently Closed section, that says "Compact after Release". I'd also like a "Brief Replies" checkbox that appends "reply briefly unless I just told you to give me full detail" to anything I send/type in.

Publish with new subversion when ready

Module: Gaming & SimEffects
Task: We need to move the feature of the submodule ForzaWheel, "Input Overlay" to be it's own submodule for SimEffects. Then we need 2 ways, the way it works now where it just reads the device (you'd specify which device to read from) or the default way of reading the data from the game rather than the device. So we need to add the code to read the telemetry data and add three numbers to the left of the brake/throttle graphs stacked one on top of the other - these are your current speed in the center with the top showing your highest top speed over a X second window (that should be a setting, default to 5) and the bottom number would be your lowest speed over that same X seconds (using the same setting of "Window Size"). So if you slow to say 50mph at the moment the bottom and middle numbers would say 50 - you then accelerate with the middle number increasing but the bottom number would stay frozen for X seconds then "catch up" to the current speed, with the top speed working the same.

Like other submodules we need a on/off with a binding button (please reuse our API) and to show that it's active in the header. Like the "Shaker" submodule we need the same, exact "Profile" box (visually the same, please link to the code rather than rebuild) so that this submodule can be auto enabled when a game runs. Then add that same feature to both Arduino and AmbiLight submodules)

When ready publish as 3.17

did you run a compact?

Module: SimEffects
Submodule: Shaker
Issue: Remove the check box for "titlebar status" as we ALWAYS want it to have titlebar status

Module: SimEffects
Issue: You added "Titlebar status" check boxes to the Input Overlay and Arduino - remove this, yes they always report to the titlebar, we don't need this option

Module: Main API
Issue: When deleting a widget by clicking on the trash can icon it's instantly deleting rather than using our delete process in the API so we get a confirm before deleting - in this case I'm thinking a red X. Also you should be able to right click on items in Hardware - Performance just like we did in Network - Zabbix - and since you likely built different code for each let's move that to the API so that both modules are calling the same base code so any widget we create gets run through the same code so we can affect them all with one change

Module: Claude/AI
Issue: The Usage block - let's change "Back in 3 h 50 min" to "Back @ 2:10PM" showing the time that account's time resets, formatted like I showed.

When ready publish as next subversion

Problem - you're trying to read the data separately for the "Input Overlay", rather we need a base set of code/API that read the data from the game/UDP such that various modules can all plug into that data, so the module gets the data and all submodules can read that data. While we do this we need to cahnge the profiles - right now we have profiles for each submodule - rather we need to have profiles at the main module level so that EVERYTHING in the SimEffects module will change for that game/app. That way we get the data once and all submodules can read the data and everything switches when a different game/app runs. So we need a profile manager then each submodule gets assigned to those profiles (so there's a "Profiles" tab/submodule then in the top right of that row there should be a Profile: (DROPDOWN) with all the various profiles so selecting one switches all submodules to that profile and if you make changes they get saved to that profile's settings for that module.

When ready publish as 3.18

continue

Module: SimEffects
Submodule: Input Overlay
Issue: The three numbers for speed, over on the left, should all be the same size as the center number

Submodule: Profiles
In the "Sub-modules" panel change to "Auto Start Modules" if that's what that means, when a process is detected and a profile is activated (or activated manually) then those sub-modules activate too. When then need a Play/Stop button (switches between the two, colored green/red) so that you can manually enable/disable an entire profile (it should have the joystick icon binding button to the right of it). Then in each submodule if the overall module is disabled the "ON" should show gray/disabled in some way indicating that it is on but that the entire profile is dark.

publish as next subversion when ready

Module: Main App
Item: Notificatoin Panel
Issue: Let's always put Claude/AI's after all other notifications

Release as next subversion when ready

Module: SimEffects
Submodule: Shaker
Issue: In the Devices block on the left move the Test button to the left of the volume level bar (on the same line, just make the bar smaller) and put the Remove button in the bottom right vs left.

Module: SimEffects
Submodule: Input Overlay
Issue: The speed numbers that are on the left - put them between the bar graphs for then inputs and the steering wheel icon. Also make the steering wheel icon a medium gray instead of white, and the numbers should be green for the top, white middle and oranage bottom. Also I should be able to right click on the overlay just like all the other widgets (reference the main API) as I don't want to rebuild the same code over and over. When right clicking you should then be able to move/resize just like the widget for the Shaker submodule

Module: Gaming
Submodule: ForzaWheel
Remove "Start it when Daisy's App starts" as we don't need that feature
Remove "Show ForzaWheel in teh title bar...." as it should always show in the titlebar if it's running

publish as next subversion when ready

Module: Video
Submodule: Mini Mirror
New Feature: For each mini mirror and/or group just like having a shortcut key to show/hide the mirror we need a process watcher that would enable/disable the submodule just like clicking the button at the top. That way it can auto turn on (but not necessarily show a mirror) so that you can then use the mirrors in that app/game

publish as next subversion when ready

There is a zip file in downloads that is a log from another machine that was crashing - can you analyze and fix accordingly?

Module: Video
Submodule: Mini Mirror
Issue: "Switch on With" (rename to "Process Watcher") has "Add a running program" button (change to "Select App") but then no "Add" button with a list of all the apps/exes that would switch on the profile

Module: Claude/AI
Bug: When pasting an image into the chat/text input box there is no way to then delete that image after pasting, you can't delete/backspace to remove it. I'd like a simple x somewhere ont he image to be able to remove it from the chat.

Module: Video
Submodule: Resize
Feature: When we active a profile, specifically via hotkey, if we press that key agian it would toggle the game/app back to un-fullscreen, so when applying we'd need to get the size/placement of the window first in the hopes we can return it to that size.

publish as next subversion when ready

Look in Downloads at the zip file and png file - they show an error and the log, if there is a bug to fix publish as next subversion when ready otherwise tell me what's wrong

continue

continue

New Module: Support (be sure to create new directories, README.md, etc. etc.)
Submodule: Networking Tools (a list of several tools below that would each be their own submodules)

Descriptoin: Network Troubleshooting module/submodules - would include tools to show ping times to common destinations (your gateway and DNS servers being some of them). A bandwidth test tool and a visual traceroute tool that would show the data in a table (with reverse name lookup on the IP addresses) along with a map based display overlaid on someting like Google Maps or if easier a map of our own custom code, I'm looking for a rough visual represntation of the locations so we don't need pinpoint accuracy.

Include information about the current connection including the type/speed and wifi details (if they exist) - I know Android devices can run apps that show all the various wifi signals in the area by channel, etc. on a graph with strength, etc. - if you can do that awesome (let the user specify which NIC will do the scanning). A basic NMAP style port scanner would be another tool with basic descriptions of known ports that link to an external source for more details on that port.

If there are any other tools you can think of that would help in network troulbeshooting, specicially when a connection is "flakey" please add them. With the ping/bandwidth tools we should be able to log those over a period of time and at a specific interval (both in settings for the submodule) and with the ability to enable/disable the logging. This should look somehwat similar to the "Network Tests" and "Speed Test" blocks on the Hardware -> Performance submodule. They should also be able to be made into widgets using our existing API, don't recode that.

Publish as 3.19 when ready

continue

continue

continue

Changes
Submodule: Traceroute
Details: should be able to double click on an entry in the table to open a popup window with more details, the location on a map, names and anything we can pull from public databases about that IP/name. When single clicking on an entry on the table that hop on the map should be highlighted 

Submodule: Speed Test
New Feature: Ability to schedule how often and for how long the test run so they are logged on regular intervals. Same gear icon settings button that allows for how often and how long and the how long setting should be used when manually clicking "Run Now"

All Submodules:
New Feature: Anywhere there is an IP Address or host name you should be able to highlight it for copy/paste or right click and "Copy IP" or "Copy Host" to copy it to the clipboard

Submodule: Port Scan
Details: Similar to traceroute we should be able to double click on an entry in the table to open a pop open window with additional details on the service, etc.

New Submodule: Host Scan
Details: Would use any avilalbe tools to probe a device to return as much as possible about it, host name, OS, version, if it's a TV, router, printer, computer, tablet, phone and then anything we can probe about it - being as invasive as possible to find as much information as possible as this will be used for IDS testing against machines I own so I want to get as much as we can (within reason) so I can harden these systems against attack.

Once a host has been scanned it would show all this with links to appriopraite additional information that said I'd like to keep as much in the app as possible with as visual a represntation of the data you can give.

Module: Tools
New Submodule: DiskMark
Details: Benchmark tools that replicate the test that Crystal DiskMark 8.0 does - with the ability to choose the size/etc so you can choose how long/hard the test would be. It would the log tests with the ability to schedule tests in the future (which shold be off by default). Show the various drives in a block on the left with the results changing based on which drive you select and if there hasn't been a run nothing gets shown. Give status during the run in a visually interesting way, perhas it changes based on the type of thing the test is doing, etc. Build visual charts, etc. with read/write speeds in GB/sec (or MB/sec with an adjustable scale in main settings).

Publish as 3.21 when ready

Getting this error in the logs - if it's a bug you can fix do so and publish as next subversion

2026-10-27 23:34:09.883   Error   DaisysApp   thread 2

Unobserved task exception: System.AggregateException: A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property. As a result, the unobserved exception was rethrown by the finalizer thread. (No such host is known.)
 ---> System.Net.Sockets.SocketException (0x00002AF9): No such host is known.
   at System.Net.Dns.GetHostEntryOrAddressesCore(IPAddress address, Boolean justAddresses, AddressFamily addressFamily, Nullable`1 startingTimestamp)
   at System.Net.Dns.<>c.<GetHostEntryAsync>b__7_0(Object s, Int64 startingTimestamp)
   at System.Net.Dns.<>c__DisplayClass39_0`1.<RunAsync>b__0(Task <p0>, Object <p1>)
   at System.Threading.Tasks.ContinuationResultTaskFromTask`1.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
--- End of stack trace from previous location ---
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   --- End of inner exception stack trace ---

Module: Tools
New Submodule: Tools
Details: Various buttons/tools that we'll build over time - the first is a button that resets the windows desktop, the same as ctrl+shift+win+B (I think that's the shortcut). If there are other reset or admin style feature that you can add buttons for go ahead.

Publish as next sub version when ready

## 2026-10-28

Module: Support
Submodule: Stress Tests
Details: A suite of stress testing tools that will produce a score at the end of a run based on the performance of that test. These don't need to be overly complex tests, just basics that would show single core, multicore, RAM, VRAM, GPU encode, decode, 3D, etc. etc. to both score a system but also to stress one to see if anything breaks. We should have 2 levels of logging, "Standard" and "Maximum" with the appropriate logging enabled during runs, again to help find issues if we break/crash things. This logs to our existing log submodule. After a run is complete you should be able to export the results of that run - then create a button that opens a pop open window that allows you to select either 2 local runs to compare them to each other or to compare a local run to an uploaded/exported run from another machine and create a page with a report of the differences between the two machines in terms of percentages, performacne, etc. as relevant. On this report include the CPU, GPU, VRAM, RAM, Storage, etc. for both systems.

Publish as 3.22 when ready

Module: Claude/AI

Module: Claude/AI
Issue: I'm concerned we are wasting tokens and doing things inefficently with what data we're sending, etc. - analzye how the app is actually working now and see if there are changes we should make to make things more efficient when coding so we're using as little tokens as are necessary, etc. - don't go crazy and make thing worse but if we can store stuff locally like we are to save great - remember we need the ability to switch accounts and pick right back up where we were when we hit a limit.

If there are code changes release as next subversion when ready

Module: Support
Submodule: Networking Tools
Issue: Rename tab/module to "Network Tools"

New Feature: We're going to add a new checkbox on the "Host Scan" screen for "deep scan", the details are:

# Feature Brief: Deep Scan (Host Scan submodule)

## Summary
Add a "Deep Scan" capability to the Host Scan submodule. Its purpose is origin-server discovery: finding the real IP/hostname of a service sitting behind a reverse proxy / CDN (Cloudflare, etc.) using only publicly available data.

## UI changes
- Add a **"Deep Scan"** checkbox to the Host Scan submodule.
- When the checkbox is **checked**, reveal a new box to the **right of the existing "Summary" box**, titled **"Deep Scan"**. This box displays Deep Scan progress and results.
- When **unchecked**, the Deep Scan box is hidden and no deep-scan work runs.

## What Deep Scan does (data sources to ingest)
1. **Historical DNS** — query passive-DNS / history providers (SecurityTrails, ViewDNS.info) for prior A records that predate the proxy, and any still-pointing-direct records.
2. **Certificate transparency** — pull crt.sh (and Censys cert search) for the domain + wildcards to enumerate hostnames and spot certs served from non-proxy IPs.
3. **Internet scan data** — query Censys/Shodan for hosts presenting the site's TLS cert, matching page title, or favicon hash, filtered to exclude known CDN ranges.
4. **Subdomain enumeration** — crt.sh + brute/resolve (Amass-style); resolve each subdomain and flag any with a non-CDN A record (common leaks: `mail.`, `ftp.`, `cpanel.`, `direct.`, `origin.`, `dev.`).
5. **Mail records** — MX, SPF/TXT; mail servers and SPF include-hosts are frequently the unproxied origin or share its network.

## Candidate validation
For each discovered candidate IP:
- Request the site directly (`https://<candidate-ip>` with `Host:` header set to the real domain, cert verification off).
- Compare response (status, title, body hash, specific markers) against the proxied site's known-good response.
- Score/rank candidates by match confidence; surface the ranked list in the Deep Scan box.

## Known limitations (surface to user, don't hide)
- A hardened origin leaks nothing: firewall allowlisting CDN IPs + Authenticated Origin Pulls means no public method recovers it. Report "no origin found" honestly rather than returning false positives.
- Respect provider API rate limits and keys; make SecurityTrails/Censys/Shodan keys configurable.

## Notes
- CDN IP range detection (to exclude proxy IPs) should use the CDN's published range lists, refreshed, not a hardcoded snapshot.
- Keep Deep Scan async/cancelable — these lookups are slow and network-bound.

release as next subversion when ready

## 2026-10-28

Module: SimEffects
New Submodule: Analysis

We're building a new tab that will give us a number of tools to analyze our driving, starting with specific features for Forza Horizon. We'll have a main screen of blocks, each with the various data below, with each being able to be turned into a widget (make sure to reuse the code we already have in the API for this). Those blocks/widgets are below with the internal data code I got from the FH6 docs for UDP data.

Vehicle
- Make/Model: S32 CarOrdinal;
- Class: S32 CarClass;
- Performance: S32 CarPerformanceIndex;
- Drive: S32 DrivetrainType;
- Motor: S32 NumCylinders;
- HP: F32 Power;
- TQ: F32 Torque;
- Boost: F32 Boost;
- Group: U32 CarGroup;

Tires
- FL: F32 TireTempFrontLeft;
- FR: F32 TireTempFrontRight;
- RL: F32 TireTempRearLeft;
- RR: F32 TireTempRearRight;

Laps
- Last: F32 LastLap;
- Best: F32 BestLap;
- Curr: F32 CurrentLap;
- Race: F32 CurrentRaceTime;
- Lap: U16 LapNumber;
- Position: U8 RacePosition;

When creating a widget you should be able to enable/disable the various datapoints in that block, so in edit mode put checkboxes next to each data type in the legend so they can be turned on/off/hidden when the widget is active.

I'd like to be able to track lap times by car/track/class so that it can be referenced in the future. As I understand nothing in the data tells you which track you're racing on, would it be possible to use X,Y,Z coordinates and a prompt for the user to name that location (the first time) so that it can be tracked? Any other creative way to know what track is what? Then we'll use that stored data to be able to see stats on cars, tracks, fastest laps, etc. etc. - give me some basic stuff now and we'll grow it out after that.

Much of this is specific to Forza Horizon so it'll obviously be part of that profile, don't worry about profiles for the other games just yet, we'll adapt this when it's ready.

Publish as 3.23 when ready

continue

Module: Support
Submodule: Wi-Fi
Issue: The top WiFi Networks graph shows each wifi and what channel, ets it's using - the spacing/graph for 2.4 Ghz works great - but for 5/6 it doesn't, if there are multiple devices on a channel they are just on top of each other so I can't see what it's actually showing. We also need to move things around, put the "Access Points" block first, "Wi-Fi Networks" next (rname to "Networks") and "Signal" last - but rather than being full width blocks let's make them 1/3 each, side-by-side, then whatever AP you select on the right highlights the details to the right.

publish as next subversion when ready
