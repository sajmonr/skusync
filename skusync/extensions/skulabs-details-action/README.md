# skulabs-details-action

An admin **action** extension on the product detail page. It shows every variant of the product with
its SkuLabs link, in a modal launched from the **More actions** menu.

Target: `admin.product-details.action.render`.

## Why this is a separate extension

It renders the same view as the product-page card in
[`../product-skulabs-details`](../product-skulabs-details) and would naturally be a second target there.
It cannot be: **an extension may not hold a block target and an action target together.**

```
Invalid extension point(s) configured:
- admin.product-details.block.render, admin.product-variant-details.block.render cannot be configured
  in the same extension as admin.product-details.action.render. Create a new extension with the
  admin.product-details.block.render, admin.product-variant-details.block.render extension point(s).
```

Don't take that message's advice literally — the two block targets can't share an extension either, so
SkuSync's three surfaces are three extensions with one target each. Both rules, and the fact that
`shopify app build` validates neither, are documented in
[`../variant-skulabs-details/README.md`](../variant-skulabs-details/README.md).

## Why the mobile apps need this

**This is the only surface merchants get on mobile.** Inline admin blocks don't render in the Shopify
mobile apps at all; actions do, though only from the three-dot menu, which apps cannot promote to a
standalone button. Confirmed by Shopify staff on the
[dev forums](https://community.shopify.dev/t/is-there-any-way-to-surface-an-order-admin-action-directly-in-shopify-mobile-instead-of-under-the-three-dot-menu/36472).

The menu item's label is the extension's `name` from the locale files, and there is no per-target name —
which is why this extension is named "View SkuLabs details" while the two blocks are named "SkuSync".

## Layout

The whole extension is one module. Everything it renders comes from `skusync/shared`:

```
src/targets/ProductAction.tsx   s-admin-action + ProductVariants, plus a Done button that closes it
```

`s-admin-action` is the required wrapper for an action target, and its buttons go in named slots —
`slot="primary-action"`, kebab-case, not the `primaryAction` spelling the prop types use. Dismissing the
modal is `shopify.close()`.

## Everything else

The shared source layout, the SkuSync endpoints and session-token auth, CORS, the API base URL and
running the stack locally are identical across the three extensions and documented once in
[`../variant-skulabs-details/README.md`](../variant-skulabs-details/README.md).
