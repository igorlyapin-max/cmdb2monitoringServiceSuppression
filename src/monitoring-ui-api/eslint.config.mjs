export default [
  {
    ignores: ['node_modules/**']
  },
  {
    files: ['server.mjs', 'public/app.js', 'scripts/*.mjs'],
    languageOptions: {
      ecmaVersion: 2024,
      sourceType: 'module'
    },
    rules: {}
  }
];
