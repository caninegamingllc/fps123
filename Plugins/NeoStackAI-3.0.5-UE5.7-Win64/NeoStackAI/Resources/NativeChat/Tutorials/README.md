# Tutorial thumbnails

These are the unchanged 480 × 360 thumbnails returned by YouTube oEmbed. The
native cards crop only the 45-pixel top/bottom letterboxing in their UV
region. They never download images or start a video player at runtime.

| File | Video | Creator | Source |
| --- | --- | --- | --- |
| overview.jpg | [This AI Can Modify Unreal Engine Projects (NeoStack AI)](https://www.youtube.com/watch?v=04GuUbWUgZs) | [LinxmaStudio](https://www.youtube.com/@LinxmaStudio) | YouTube oEmbed thumbnail |
| claude-code.jpg | [Claude Code Setup For NeoStack AI](https://www.youtube.com/watch?v=WXhZCylkJJ0) | [LinxmaStudio](https://www.youtube.com/@LinxmaStudio) | [NeoStack's Claude Code documentation](https://aik.betide.studio/agents/claude-code) embeds this video |

The short navigation labels are "Meet NeoStack" and "Connect Claude Code".
The full video titles and credits remain in the card tooltips and catalog and
match oEmbed metadata. These optional videos show
an earlier interface; the cards disclose that and defer current setup steps to
the native guide. Opening YouTube does not mark a tutorial watched.

Replace a video ID, short label, title, description, credit, context note and thumbnail together
in `NativeChatTutorials.cpp`. Preserve verified provenance for each replacement.
No YouTube or creator artwork is presented as NeoStack-authored artwork.
