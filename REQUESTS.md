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
