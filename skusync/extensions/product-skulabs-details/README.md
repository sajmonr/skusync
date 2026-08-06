# product-skulabs-details

The admin block on the **product** detail page. It lists every variant of the product that SkuSync
knows about, each with its SkuLabs link.

Target: `admin.product-details.block.render`.

## Why this is a separate extension

It is the same kind of card as [`../variant-skulabs-details`](../variant-skulabs-details), which covers
the variant page, and would naturally be a second target there. It cannot be: **an extension may hold
only one admin block target.** `shopify app dev` rejects two:

```
Extension targeting config should have just one of the following targets
[admin.abandoned-checkout-details.block.render, … admin.product-details.block.render,
admin.product-variant-details.block.render]
```

`shopify app build` bundles two block targets happily — only dev and deploy validate target layout — so
a green build is not evidence that a layout is legal. See
[`../variant-skulabs-details/README.md`](../variant-skulabs-details/README.md) for both target rules and
the misleading error message that sits behind the other one.

## Why the product page needs its own card

The variant block cannot cover it:

- **A single-variant product has no variant page.** Shopify shows such a product as itself and never
  offers the variant view, so the variant block is invisible for exactly the simplest products.
- **A multi-variant product would otherwise take one page visit per variant.** This lists them
  together, ordered by SKU.

For the Shopify mobile apps neither block renders at all; that surface is
[`../skulabs-details-action`](../skulabs-details-action).

## Layout

The whole extension is one module. Everything it renders comes from `skusync/shared`:

```
src/targets/ProductBlock.tsx   s-admin-block + ProductVariants
```

## Everything else

The shared source layout, the SkuSync endpoints and session-token auth, CORS, the API base URL and
running the stack locally are identical across the three extensions and documented once in
[`../variant-skulabs-details/README.md`](../variant-skulabs-details/README.md).

`shopify.extension.toml` has no `uid` yet. The field is optional and the CLI generates one on the first
`shopify app dev` or `shopify app deploy`, writing it into the file. **Commit it when it appears** — it
is how a deploy maps to this extension's registration.
