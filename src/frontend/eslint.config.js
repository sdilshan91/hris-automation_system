// @ts-check
const eslint = require("@eslint/js");
const tseslint = require("typescript-eslint");
const angular = require("angular-eslint");

module.exports = tseslint.config(
  {
    // Generated output is NOT source — never lint it, and never let `--fix` touch it.
    // `src/app/core/api/generated/` is emitted by `npm run api:types` (openapi-typescript)
    // from contracts/openapi/hrm-v1.json. It accounted for 1433 of the 1749 findings on the
    // first run, all `consistent-indexed-object-style`. Auto-fixing them would rewrite the
    // generated file so `npm run api:types:check` fails against a freshly generated one —
    // i.e. it would break the FE/BE contract-drift gate this repo depends on.
    // NOTE: this block adds `ignores` ONLY. No rule severity is lowered here.
    ignores: [
      "src/app/core/api/generated/**",
      ".angular/**",
      "dist/**",
      "coverage/**",
    ],
  },
  {
    files: ["**/*.ts"],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      "@angular-eslint/directive-selector": [
        "error",
        {
          type: "attribute",
          prefix: "app",
          style: "camelCase",
        },
      ],
      "@angular-eslint/component-selector": [
        "error",
        {
          type: "element",
          prefix: "app",
          style: "kebab-case",
        },
      ],
    },
  },
  {
    files: ["**/*.html"],
    extends: [
      ...angular.configs.templateRecommended,
      ...angular.configs.templateAccessibility,
    ],
    rules: {},
  }
);
