// Types the `shopify` API object that the extension host injects as a global.
//
// The CLI-generated shopify.d.ts already does this, but only via one `declare module` block per file it
// found under src/ at the last build. Two consequences, and this file exists for both: the shared code
// in skusync/shared is outside src/ and so gets no generated block at all, and the generated blocks
// only cover files that existed at the *last* build, so without this a new source file breaks `tsc`
// until someone runs a build. The two files don't conflict; they populate different scopes.
//
// One target per extension is a platform limit, so this is the exact API type rather than an
// intersection, and `extension.target` keeps its real literal type.
declare const shopify: import("@shopify/ui-extensions/admin.product-details.block.render").Api;
