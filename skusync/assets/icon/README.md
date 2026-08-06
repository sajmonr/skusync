# App icon

The SkuSync app icon: two systems holding the same SKU, with traffic both ways.

| File | Use |
| --- | --- |
| `app-icon.svg` | The source. Edit this, never the PNG. |
| `app-icon-1200.png` | The 1200 × 1200 export that gets uploaded. |

## Uploading it

The icon is **not** part of `shopify.app.toml` — there is no icon field in the app config. Upload
`app-icon-1200.png` on the app's setup page in the Shopify dev/Partner dashboard. Nothing in this repo
references it at build time, which is exactly why the source is kept here: without it, the next person
who needs a favicon or a listing banner has to redraw the mark.

The PNG carries an alpha channel, so the margin around the badge is transparent rather than baked white.
That is deliberate — it composites correctly wherever Shopify puts it.

## Re-rendering the PNG

No SVG rasteriser is installed on this project's machines, but Chrome will do it exactly:

```sh
cd skusync/assets/icon
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" \
  --headless --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
  --default-background-color=00000000 --window-size=1200,1200 \
  --screenshot=app-icon-1200.png app-icon.svg
sips -g pixelWidth -g pixelHeight app-icon-1200.png   # expect 1200 x 1200
```

`--force-device-scale-factor=1` matters: on a Retina display Chrome otherwise renders at 2× and you get
a 2400 px file that Shopify rejects.

## The spec, and why the geometry isn't arbitrary

Shopify requires a 1200 × 1200 PNG or JPEG, with the artwork filling 10/16–12/16 of the canvas and a
1/16 margin (75 px) free of any visual element. The badge here is 880 px — 11/16, mid-range — leaving a
160 px margin. Icons render on white and light grey, so the dark ground is what keeps the mark from
floating on either.

Palette: slate `#1E293B`, amber `#F5A524`, white `#FFFFFF`.

## Three things not to "tidy up"

Learned by drawing the alternatives, so they don't get rediscovered the hard way:

1. **The tiles stay on a diagonal.** Side by side above the arrows, the mark becomes a face — two eyes
   and a smile — and it cannot be unseen once noticed.
2. **Both tiles carry the same bars.** A blank second tile reads as content that failed to load, which
   is the opposite of what the app does. Two identical readings is the point.
3. **No text in the icon.** Shopify advises against it and a wordmark turns to mush by 32 px, which is
   the size the icon actually spends its life at.

An earlier teal version was dropped because its green sat close to Shopify's own `#008060`. Partners may
not imply endorsement, so a near-match on a green badge is a needless conversation during app review.
