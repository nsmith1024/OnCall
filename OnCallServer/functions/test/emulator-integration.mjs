import assert from "node:assert/strict";

const suffix = Date.now();
const authUrl = "http://127.0.0.1:9099/identitytoolkit.googleapis.com/v1/accounts:signUp?key=test";
const functionsUrl = "http://127.0.0.1:5001/oncall-564dc/us-central1";

async function json(response) {
  const body = await response.json();
  if (!response.ok) throw new Error(`${response.status}: ${JSON.stringify(body)}`);
  return body;
}

async function register(kind) {
  return json(await fetch(authUrl, {
    method: "POST", headers: {"content-type": "application/json"},
    body: JSON.stringify({email: `${kind}-${suffix}@example.test`, password: "testing123", returnSecureToken: true}),
  }));
}

async function call(name, token, body) {
  return json(await fetch(`${functionsUrl}/${name}`, {
    method: body === undefined ? "GET" : "POST",
    headers: {authorization: `Bearer ${token}`, "content-type": "application/json"},
    body: body === undefined ? undefined : JSON.stringify(body),
  }));
}

const lawyer = await register("lawyer");
assert.equal((await call("seedDemoLawyer", lawyer.idToken, {})).role, "lawyer");
assert.equal((await call("setLawyerAvailability", lawyer.idToken, {available: true})).available, true);

const client = await register("client");
assert.equal((await call("getMyProfile", client.idToken)).role, "client");
const request = await call("createLegalRequest", client.idToken, {
  incidentType: "traffic", latitude: 42.6526, longitude: -73.7562, city: "Albany", state: "NY",
});
assert.equal(request.status, "searching");
assert.equal(request.offeredLawyerCount, 1);

const offers = await call("getMyOffers", lawyer.idToken);
assert.equal(offers.offers.length, 1);
assert.equal(offers.offers[0].requestId, request.requestId);
assert.equal((await call("acceptLegalRequest", lawyer.idToken, {requestId: request.requestId})).status, "assigned");

console.log("Emulator integration passed: client profile -> matched offer -> lawyer assignment.");
