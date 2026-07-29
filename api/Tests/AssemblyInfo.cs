using Xunit;

// The suite runs several in-process ASP.NET Core hosts (WebApplicationFactory<Program>) that all validate JWT
// bearer tokens signed with the shared TestAuth.SigningKey. Disposing one host mid-run tears down shared
// System.IdentityModel state another still-live host depends on, so a later host spuriously rejects a valid
// token with 401. Two coordinated measures make the suite deterministic:
//   1. Each host factory suppresses per-fixture disposal (no-op Dispose override), keeping every host alive
//      for the whole run so nothing is torn down while another host is still in use.
//   2. The assembly runs serially (below), so hosts are created and used in a single, non-overlapping order.
// The full run completes in about a second.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
