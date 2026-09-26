// Bots inserted by scripts/seed.mjs and asserted by the tests.
// The tokens are deliberately not decryptable ("v1:" envelope with a bogus payload), so the API
// never contacts Discord for them and the startup legacy-token migration leaves them alone.
export const SEEDED_BOTS = [
  { name: 'E2E Public Bot', token: 'v1:e2e-not-a-real-token', isPublic: true },
  { name: 'E2E Private Bot', token: 'v1:e2e-not-a-real-token', isPublic: false }
];
