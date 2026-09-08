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

async function status(name, token, body) {
  return (await fetch(`${functionsUrl}/${name}`, {
    method: body === undefined ? "GET" : "POST",
    headers: {authorization: `Bearer ${token}`, "content-type": "application/json"},
    body: body === undefined ? undefined : JSON.stringify(body),
  })).status;
}

const lawyer = await register("lawyer");
assert.equal((await call("seedDemoLawyer", lawyer.idToken, {})).role, "lawyer");
assert.equal((await call("setLawyerAvailability", lawyer.idToken, {available: true})).available, true);
assert.equal(typeof (await call("registerDevice", lawyer.idToken, {platform: "android", token: `test-token-${suffix}`})).deviceId, "string");

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
const activeSummary = (await call("getMyActiveRequest", client.idToken)).request;
assert.equal(activeSummary.status, "assigned");
assert.equal("location" in activeSummary, false);
const assignedDetails = await call("getAssignedRequestDetails", lawyer.idToken, {requestId: request.requestId});
assert.equal(assignedDetails.location.latitude, 42.6526);
assert.equal(assignedDetails.location.longitude, -73.7562);
assert.equal(await status("getAssignedRequestDetails", client.idToken, {requestId: request.requestId}), 403);
const clientMeeting = await call("getMeetingSession", client.idToken, {requestId: request.requestId});
const lawyerMeeting = await call("getMeetingSession", lawyer.idToken, {requestId: request.requestId});
assert.equal(clientMeeting.room, lawyerMeeting.room);
assert.equal(clientMeeting.token.split(".").length, 3);
assert.equal((await call("getMyActiveRequest", client.idToken)).request.status, "assigned");
assert.equal((await call("completeLegalRequest", lawyer.idToken, {requestId: request.requestId})).status, "completed");
assert.equal(await status("getAssignedRequestDetails", lawyer.idToken, {requestId: request.requestId}), 409);

await call("setLawyerAvailability", lawyer.idToken, {available: true});
const cancellingClient = await register("cancelling-client");
await call("getMyProfile", cancellingClient.idToken);
const cancellingRequest = await call("createLegalRequest", cancellingClient.idToken, {
  incidentType: "criminal", latitude: 42.6526, longitude: -73.7562, city: "Albany", state: "NY",
});
assert.equal((await call("cancelLegalRequest", cancellingClient.idToken, {requestId: cancellingRequest.requestId})).status, "cancelled");
assert.equal((await call("getMyActiveRequest", cancellingClient.idToken)).request, null);

console.log("Emulator integration passed: profile, presence, matching, assignment, cancellation, and Jitsi authorization.");
