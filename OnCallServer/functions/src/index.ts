import {setGlobalOptions} from "firebase-functions/v2";
import {HttpsOptions, onRequest, Request} from "firebase-functions/v2/https";
import {onDocumentCreated} from "firebase-functions/v2/firestore";
import {defineSecret} from "firebase-functions/params";
import * as admin from "firebase-admin";
import {DecodedIdToken, getAuth} from "firebase-admin/auth";
import {FieldValue, GeoPoint, getFirestore} from "firebase-admin/firestore";
import {getMessaging} from "firebase-admin/messaging";
import {Response} from "express";
import {createHash, createHmac, randomUUID} from "node:crypto";
import {reverseGeocode} from "./geocoding/index.js";

admin.initializeApp();
const db = getFirestore();
const jitsiJwtSecret = defineSecret("JITSI_JWT_SECRET");
setGlobalOptions({region: "us-central1", maxInstances: 10});

type Handler = (req: Request, res: Response) => Promise<void>;

function requiredText(value: unknown, field: string, maxLength: number): string {
  if (typeof value !== "string" || !value.trim() || value.trim().length > maxLength) {
    throw new Error(`INVALID_${field.toUpperCase()}`);
  }
  return value.trim();
}

function requiredNumber(value: unknown, field: string, min: number, max: number): number {
  if (typeof value !== "number" || !Number.isFinite(value) || value < min || value > max) {
    throw new Error(`INVALID_${field.toUpperCase()}`);
  }
  return value;
}

async function authenticate(req: Request): Promise<DecodedIdToken> {
  const header = req.header("authorization") ?? "";
  if (!header.startsWith("Bearer ")) throw new Error("UNAUTHENTICATED");
  return getAuth().verifyIdToken(header.substring(7));
}

function statusFor(error: unknown): number {
  const message = error instanceof Error ? error.message : "UNKNOWN";
  if (message === "UNAUTHENTICATED" || message.includes("ID token")) return 401;
  if (message === "FORBIDDEN") return 403;
  if (message === "NOT_FOUND") return 404;
  if (message === "CONFLICT") return 409;
  if (message.startsWith("INVALID_")) return 400;
  return 500;
}

function endpoint(handler: Handler, options?: HttpsOptions) {
  return onRequest(options ?? {}, async (req, res) => {
    res.set("Access-Control-Allow-Origin", "*");
    res.set("Access-Control-Allow-Headers", "Authorization, Content-Type");
    res.set("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
    if (req.method === "OPTIONS") {
      res.status(204).send("");
      return;
    }
    try {
      await handler(req, res);
    } catch (error) {
      res.status(statusFor(error)).json({
        error: error instanceof Error ? error.message : "UNKNOWN",
      });
    }
  });
}

export const getMyProfile = endpoint(async (req, res) => {
  const user = await authenticate(req);
  const ref = db.collection("users").doc(user.uid);
  let profile = await ref.get();
  if (!profile.exists) {
    await ref.create({
      email: user.email ?? null,
      displayName: user.email?.split("@")[0] ?? "OnCall user",
      role: "client",
      accountStatus: "active",
      createdAt: FieldValue.serverTimestamp(),
      updatedAt: FieldValue.serverTimestamp(),
    });
    profile = await ref.get();
  }
  res.status(200).json({id: profile.id, ...profile.data()});
});

export const updateMyProfile = endpoint(async (req, res) => {
  if (req.method !== "POST") throw new Error("INVALID_METHOD");
  const user = await authenticate(req);
  const displayName = requiredText(req.body?.displayName, "displayName", 80);
  const ref = db.collection("users").doc(user.uid);
  const profile = await ref.get();
  if (!profile.exists) {
    await ref.create({
      email: user.email ?? null, displayName, role: "client", accountStatus: "active",
      createdAt: FieldValue.serverTimestamp(),
      updatedAt: FieldValue.serverTimestamp(),
    });
  } else {
    await ref.update({displayName, updatedAt: FieldValue.serverTimestamp()});
  }
  res.status(200).json({ok: true});
});

export const registerDevice = endpoint(async (req, res) => {
  if (req.method !== "POST") throw new Error("INVALID_METHOD");
  const user = await authenticate(req);
  const token = requiredText(req.body?.token, "token", 4096);
  const platform = requiredText(req.body?.platform, "platform", 20).toLowerCase();
  if (!["android", "ios"].includes(platform)) throw new Error("INVALID_PLATFORM");
  const deviceId = createHash("sha256").update(token).digest("hex");
  await db.collection("users").doc(user.uid).collection("devices").doc(deviceId).set({
    token, platform, enabled: true, updatedAt: FieldValue.serverTimestamp(),
  }, {merge: true});
  res.status(200).json({deviceId});
});

function signJwt(payload: Record<string, unknown>, secret: string): string {
  const encode = (value: unknown) => Buffer.from(JSON.stringify(value)).toString("base64url");
  const unsigned = `${encode({alg: "HS256", typ: "JWT"})}.${encode(payload)}`;
  return `${unsigned}.${createHmac("sha256", secret).update(unsigned).digest("base64url")}`;
}

export const createLegalRequest = endpoint(async (req, res) => {
  if (req.method !== "POST") throw new Error("INVALID_METHOD");
  const user = await authenticate(req);
  const incidentType = requiredText(req.body?.incidentType, "incidentType", 60).toLowerCase();
  const latitude = requiredNumber(req.body?.latitude, "latitude", -90, 90);
  const longitude = requiredNumber(req.body?.longitude, "longitude", -180, 180);
  const cityHint = requiredText(req.body?.city, "city", 100);
  const stateHint = requiredText(req.body?.state, "state", 40).toUpperCase();
  const jurisdiction = await reverseGeocode({latitude, longitude}, {city: cityHint, state: stateHint});
  const {city, state} = jurisdiction;
  const profile = await db.collection("users").doc(user.uid).get();
  if (!profile.exists || profile.get("role") !== "client" || profile.get("accountStatus") !== "active") {
    throw new Error("FORBIDDEN");
  }
  const existing = await db.collection("legalRequests")
    .where("clientId", "==", user.uid)
    .where("status", "in", ["searching", "assigned"]).limit(1).get();
  if (!existing.empty) throw new Error("CONFLICT");
  const ref = db.collection("legalRequests").doc();
  await ref.create({
    clientId: user.uid, incidentType,
    location: new GeoPoint(latitude, longitude), city, state,
    county: jurisdiction.county ?? null, postalCode: jurisdiction.postalCode ?? null,
    geocodingProvider: jurisdiction.provider, locationVerifiedByServer: jurisdiction.verifiedByServer,
    status: "searching", assignedLawyerId: null,
    createdAt: FieldValue.serverTimestamp(),
    updatedAt: FieldValue.serverTimestamp(),
  });

  const candidates = await db.collection("lawyers").where("acceptingRequests", "==", true).limit(50).get();
  const presenceCutoff = Date.now() - 2 * 60_000;
  const matches = candidates.docs.filter((lawyer) => {
    const licensedStates = lawyer.get("licensedStates") as string[] | undefined;
    const practiceAreas = lawyer.get("practiceAreas") as string[] | undefined;
    const serviceCities = lawyer.get("serviceCities") as string[] | undefined;
    const lastPresenceAt = lawyer.get("lastPresenceAt") as admin.firestore.Timestamp | undefined;
    return lawyer.get("verificationStatus") === "approved" && (lastPresenceAt?.toMillis() ?? 0) >= presenceCutoff && licensedStates?.includes(state) &&
      practiceAreas?.includes(incidentType) && (!serviceCities?.length || serviceCities.includes(city));
  }).slice(0, 3);
  if (matches.length) {
    const batch = db.batch();
    for (const lawyer of matches) {
      const offerRef = db.collection("requestOffers").doc(`${ref.id}_${lawyer.id}`);
      batch.create(offerRef, {
        requestId: ref.id, lawyerId: lawyer.id, incidentType, city, state,
        status: "offered", createdAt: FieldValue.serverTimestamp(),
        expiresAt: new Date(Date.now() + 30_000),
      });
      batch.create(db.collection("notificationOutbox").doc(), {
        type: "legal_request_offer", userId: lawyer.id, requestId: ref.id,
        title: "New legal request", body: `${incidentType} assistance requested in ${city}`,
        status: "pending", createdAt: FieldValue.serverTimestamp(),
      });
    }
    await batch.commit();
  }
  res.status(201).json({requestId: ref.id, status: "searching", offeredLawyerCount: matches.length});
});

export const getMyActiveRequest = endpoint(async (req, res) => {
  const user = await authenticate(req);
  const active = await db.collection("legalRequests").where("clientId", "==", user.uid)
    .where("status", "in", ["searching", "assigned"]).limit(1).get();
  if (active.empty) {
    res.status(200).json({request: null});
    return;
  }
  const request = active.docs[0];
  res.status(200).json({request: {id: request.id, ...request.data()}});
});

export const getMyActiveAssignment = endpoint(async (req, res) => {
  const user = await authenticate(req);
  const assigned = await db.collection("legalRequests").where("assignedLawyerId", "==", user.uid)
    .where("status", "==", "assigned").limit(1).get();
  if (assigned.empty) {
    res.status(200).json({request: null});
    return;
  }
  const request = assigned.docs[0];
  res.status(200).json({request: {id: request.id, ...request.data()}});
});

export const cancelLegalRequest = endpoint(async (req, res) => {
  if (req.method !== "POST") throw new Error("INVALID_METHOD");
  const user = await authenticate(req);
  const requestId = requiredText(req.body?.requestId, "requestId", 128);
  const requestRef = db.collection("legalRequests").doc(requestId);
  await db.runTransaction(async (transaction) => {
    const legalRequest = await transaction.get(requestRef);
    if (!legalRequest.exists) throw new Error("NOT_FOUND");
    if (legalRequest.get("clientId") !== user.uid) throw new Error("FORBIDDEN");
    if (!["searching", "assigned"].includes(legalRequest.get("status") as string)) throw new Error("CONFLICT");
    const assignedLawyerId = legalRequest.get("assignedLawyerId") as string | null;
    const assignedLawyerRef = assignedLawyerId ? db.collection("lawyers").doc(assignedLawyerId) : null;
    if (assignedLawyerRef) await transaction.get(assignedLawyerRef);
    transaction.update(requestRef, {
      status: "cancelled", cancelledAt: FieldValue.serverTimestamp(), updatedAt: FieldValue.serverTimestamp(),
    });
    if (assignedLawyerRef) transaction.update(assignedLawyerRef, {
      acceptingRequests: false, currentRequestId: null, updatedAt: FieldValue.serverTimestamp(),
    });
  });
  const offers = await db.collection("requestOffers").where("requestId", "==", requestId).where("status", "==", "offered").get();
  if (!offers.empty) {
    const batch = db.batch();
    offers.docs.forEach((offer) => batch.update(offer.ref, {status: "cancelled"}));
    await batch.commit();
  }
  res.status(200).json({requestId, status: "cancelled"});
});

export const getMyOffers = endpoint(async (req, res) => {
  const user = await authenticate(req);
  const profile = await db.collection("users").doc(user.uid).get();
  if (!profile.exists || profile.get("role") !== "lawyer") throw new Error("FORBIDDEN");
  const offers = await db.collection("requestOffers").where("lawyerId", "==", user.uid)
    .where("status", "==", "offered").limit(20).get();
  const now = Date.now();
  const current = offers.docs.filter((offer) => {
    const expiresAt = offer.get("expiresAt") as admin.firestore.Timestamp | undefined;
    return (expiresAt?.toMillis() ?? 0) > now;
  });
  res.status(200).json({offers: current.map((offer) => ({id: offer.id, ...offer.data()}))});
});

export const setLawyerAvailability = endpoint(async (req, res) => {
  if (req.method !== "POST") throw new Error("INVALID_METHOD");
  const user = await authenticate(req);
  if (typeof req.body?.available !== "boolean") throw new Error("INVALID_AVAILABLE");
  const profile = await db.collection("users").doc(user.uid).get();
  const lawyerRef = db.collection("lawyers").doc(user.uid);
  const lawyer = await lawyerRef.get();
  if (!profile.exists || profile.get("role") !== "lawyer" || !lawyer.exists || lawyer.get("verificationStatus") !== "approved") {
    throw new Error("FORBIDDEN");
  }
  if (req.body.available && lawyer.get("currentRequestId")) throw new Error("CONFLICT");
  await lawyerRef.update({
    acceptingRequests: req.body.available,
    lastPresenceAt: FieldValue.serverTimestamp(),
    updatedAt: FieldValue.serverTimestamp(),
  });
  res.status(200).json({available: req.body.available});
});

export const acceptLegalRequest = endpoint(async (req, res) => {
  if (req.method !== "POST") throw new Error("INVALID_METHOD");
  const user = await authenticate(req);
  const requestId = requiredText(req.body?.requestId, "requestId", 128);
  const userRef = db.collection("users").doc(user.uid);
  const lawyerRef = db.collection("lawyers").doc(user.uid);
  const requestRef = db.collection("legalRequests").doc(requestId);
  const offerRef = db.collection("requestOffers").doc(`${requestId}_${user.uid}`);
  await db.runTransaction(async (transaction) => {
    const [profile, lawyer, legalRequest, offer] = await Promise.all([
      transaction.get(userRef), transaction.get(lawyerRef), transaction.get(requestRef), transaction.get(offerRef),
    ]);
    if (!profile.exists || profile.get("role") !== "lawyer" || profile.get("accountStatus") !== "active") throw new Error("FORBIDDEN");
    if (!lawyer.exists || lawyer.get("verificationStatus") !== "approved" || lawyer.get("acceptingRequests") !== true) throw new Error("FORBIDDEN");
    if (!legalRequest.exists) throw new Error("NOT_FOUND");
    if (!offer.exists || offer.get("status") !== "offered") throw new Error("FORBIDDEN");
    const expiresAt = offer.get("expiresAt") as admin.firestore.Timestamp | undefined;
    if ((expiresAt?.toMillis() ?? 0) <= Date.now()) throw new Error("CONFLICT");
    if (legalRequest.get("status") !== "searching") throw new Error("CONFLICT");
    if (!(lawyer.get("licensedStates") as string[] | undefined)?.includes(legalRequest.get("state"))) throw new Error("FORBIDDEN");
    transaction.update(requestRef, {
      status: "assigned", assignedLawyerId: user.uid,
      meetingRoom: `case-${randomUUID()}`,
      assignedAt: FieldValue.serverTimestamp(),
      updatedAt: FieldValue.serverTimestamp(),
    });
    transaction.update(offerRef, {status: "accepted", acceptedAt: FieldValue.serverTimestamp()});
    transaction.update(lawyerRef, {
      acceptingRequests: false, currentRequestId: requestId, updatedAt: FieldValue.serverTimestamp(),
    });
  });
  res.status(200).json({requestId, status: "assigned", lawyerId: user.uid});
});

export const getMeetingSession = endpoint(async (req, res) => {
  if (req.method !== "POST") throw new Error("INVALID_METHOD");
  const user = await authenticate(req);
  const requestId = requiredText(req.body?.requestId, "requestId", 128);
  const legalRequest = await db.collection("legalRequests").doc(requestId).get();
  if (!legalRequest.exists) throw new Error("NOT_FOUND");
  if (legalRequest.get("status") !== "assigned") throw new Error("CONFLICT");
  const isClient = legalRequest.get("clientId") === user.uid;
  const isLawyer = legalRequest.get("assignedLawyerId") === user.uid;
  if (!isClient && !isLawyer) throw new Error("FORBIDDEN");
  const room = legalRequest.get("meetingRoom") as string | undefined;
  if (!room) throw new Error("CONFLICT");
  const serverUrl = process.env.JITSI_SERVER_URL ?? (process.env.FUNCTIONS_EMULATOR === "true" ? "https://meet.example.invalid" : "");
  const secret = process.env.FUNCTIONS_EMULATOR === "true" ? "local-emulator-secret-not-for-production" : jitsiJwtSecret.value();
  if (!serverUrl || !secret) throw new Error("SERVICE_NOT_CONFIGURED");
  const now = Math.floor(Date.now() / 1000);
  const expires = now + 5 * 60;
  const token = signJwt({
    aud: "oncall", iss: "oncall", sub: new URL(serverUrl).hostname,
    room, nbf: now - 5, exp: expires,
    context: {user: {id: user.uid, name: user.name ?? user.email ?? "OnCall user", moderator: isLawyer ? "true" : "false"}},
  }, secret);
  res.status(200).json({serverUrl, room, token, expiresAt: new Date(expires * 1000).toISOString()});
}, {secrets: [jitsiJwtSecret]});

export const completeLegalRequest = endpoint(async (req, res) => {
  if (req.method !== "POST") throw new Error("INVALID_METHOD");
  const user = await authenticate(req);
  const requestId = requiredText(req.body?.requestId, "requestId", 128);
  const requestRef = db.collection("legalRequests").doc(requestId);
  const lawyerRef = db.collection("lawyers").doc(user.uid);
  await db.runTransaction(async (transaction) => {
    const [legalRequest, lawyer] = await Promise.all([transaction.get(requestRef), transaction.get(lawyerRef)]);
    if (!legalRequest.exists) throw new Error("NOT_FOUND");
    if (legalRequest.get("status") !== "assigned" || legalRequest.get("assignedLawyerId") !== user.uid) throw new Error("FORBIDDEN");
    if (!lawyer.exists || lawyer.get("currentRequestId") !== requestId) throw new Error("CONFLICT");
    transaction.update(requestRef, {
      status: "completed", completedAt: FieldValue.serverTimestamp(), updatedAt: FieldValue.serverTimestamp(),
    });
    transaction.update(lawyerRef, {
      currentRequestId: null, acceptingRequests: false, updatedAt: FieldValue.serverTimestamp(),
    });
  });
  res.status(200).json({requestId, status: "completed"});
});

// This helper cannot run after deployment; it exists only for local emulator testing.
export const seedDemoLawyer = endpoint(async (req, res) => {
  if (process.env.FUNCTIONS_EMULATOR !== "true") throw new Error("NOT_FOUND");
  if (req.method !== "POST") throw new Error("INVALID_METHOD");
  const user = await authenticate(req);
  await db.collection("users").doc(user.uid).set({
    email: user.email ?? null, displayName: "Demo Lawyer", role: "lawyer", accountStatus: "active",
    createdAt: FieldValue.serverTimestamp(), updatedAt: FieldValue.serverTimestamp(),
  }, {merge: true});
  await db.collection("lawyers").doc(user.uid).set({
    verificationStatus: "approved", licensedStates: ["NY"],
    practiceAreas: ["traffic", "criminal"], serviceCities: ["Albany", "Troy"],
    acceptingRequests: true, currentRequestId: null, lastPresenceAt: FieldValue.serverTimestamp(), updatedAt: FieldValue.serverTimestamp(),
  }, {merge: true});
  res.status(200).json({ok: true, role: "lawyer"});
});

export const deliverNotification = onDocumentCreated("notificationOutbox/{notificationId}", async (event) => {
  const snapshot = event.data;
  if (!snapshot) return;
  if (process.env.FUNCTIONS_EMULATOR === "true") {
    await snapshot.ref.update({status: "emulator_skipped", completedAt: FieldValue.serverTimestamp()});
    return;
  }
  const notification = snapshot.data();
  const devices = await db.collection("users").doc(notification.userId).collection("devices")
    .where("enabled", "==", true).limit(20).get();
  const tokens = devices.docs.map((device) => device.get("token") as string).filter(Boolean);
  if (!tokens.length) {
    await snapshot.ref.update({status: "no_devices", completedAt: FieldValue.serverTimestamp()});
    return;
  }
  const result = await getMessaging().sendEachForMulticast({
    tokens,
    notification: {title: notification.title, body: notification.body},
    data: {type: notification.type, requestId: notification.requestId},
    android: {priority: "high", ttl: 30_000, notification: {channelId: "legal_requests", sound: "default"}},
    apns: {
      headers: {"apns-priority": "10", "apns-expiration": String(Math.floor(Date.now() / 1000) + 30)},
      payload: {aps: {sound: "default"}},
    },
  });
  await snapshot.ref.update({
    status: result.failureCount ? "partial" : "sent",
    successCount: result.successCount, failureCount: result.failureCount,
    completedAt: FieldValue.serverTimestamp(),
  });
});
