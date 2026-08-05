// Types the `shopify` API object that the extension host injects as a global.
//
// The CLI-generated shopify.d.ts already does this, but only via one `declare module` block per file
// it found under src/ at the last build — so adding a source file breaks `tsc` until someone runs a
// build, and a CI typecheck without a preceding build fails outright. Declaring the global once here
// removes that ordering dependency. shopify.d.ts stays as the CLI writes it; the two don't conflict
// because they populate different scopes.
declare const shopify: import("@shopify/ui-extensions/admin.product-variant-details.block.render").Api;
