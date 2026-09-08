import assert from "node:assert/strict";
import http from "node:http";
import {ClientHintGeocoder} from "../lib/geocoding/client-hint-geocoder.js";
import {NominatimGeocoder} from "../lib/geocoding/nominatim-geocoder.js";

const coordinates = {latitude: 42.6526, longitude: -73.7562};
const native = await new ClientHintGeocoder().reverse(coordinates, {city: " Albany ", state: "ny"});
assert.deepEqual(native, {city: "Albany", state: "NY", provider: "client-native", verifiedByServer: false});

const server = http.createServer((request, response) => {
  const url = new URL(request.url, "http://localhost");
  assert.equal(url.pathname, "/reverse");
  assert.equal(url.searchParams.get("lat"), String(coordinates.latitude));
  response.writeHead(200, {"content-type": "application/json"});
  response.end(JSON.stringify({address: {
    city: "Albany", county: "Albany County", state: "New York",
    "ISO3166-2-lvl4": "US-NY", postcode: "12207",
  }}));
});
await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
try {
  const address = server.address();
  assert.equal(typeof address, "object");
  const result = await new NominatimGeocoder(`http://127.0.0.1:${address.port}`).reverse(coordinates);
  assert.deepEqual(result, {
    city: "Albany", state: "NY", county: "Albany County", postalCode: "12207",
    provider: "nominatim", verifiedByServer: true,
  });
} finally {
  server.close();
}

console.log("Geocoding provider unit tests passed.");
