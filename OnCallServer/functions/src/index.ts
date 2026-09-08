import {setGlobalOptions} from "firebase-functions/v2";
import {onRequest, Request} from "firebase-functions/v2/https";
import * as admin from "firebase-admin";
import {DecodedIdToken, getAuth} from "firebase-admin/auth";
import {FieldValue, GeoPoint, getFirestore} from "firebase-admin/firestore";
import {Response} from "express";

admin.initializeApp();
const db = getFirestore();
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

function endpoint(handler: Handler) {
  return onRequest(async (req, res) => {
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

export const createLegalRequest = endpoint(async (req, res) => {
  if (req.method !== "POST") throw new Error("INVALID_METHOD");
  const user = await authenticate(req);
  const incidentType = requiredText(req.body?.incidentType, "incidentType", 60).toLowerCase();
  const latitude = requiredNumber(req.body?.latitude, "latitude", -90, 90);
  const longitude = requiredNumber(req.body?.longitude, "longitude", -180, 180);
  const city = requiredText(req.body?.city, "city", 100);
  const state = requiredText(req.body?.state, "state", 40).toUpperCase();
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
    status: "searching", assignedLawyerId: null,
    createdAt: FieldValue.serverTimestamp(),
    updatedAt: FieldValue.serverTimestamp(),
  });

  const candidates = await db.collection("lawyers").where("acceptingRequests", "==", true).limit(50).get();
  const matches = candidates.docs.filter((lawyer) => {
    const licensedStates = lawyer.get("licensedStates") as string[] | undefined;
    const practiceAreas = lawyer.get("practiceAreas") as string[] | undefined;
    const serviceCities = lawyer.get("serviceCities") as string[] | undefined;
    return lawyer.get("verificationStatus") === "approved" && licensedStates?.includes(state) &&
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
    }
    await batch.commit();
  }
  res.status(201).json({requestId: ref.id, status: "searching", offeredLawyerCount: matches.length});
});

export const getMyOffers = endpoint(async (req, res) => {
  const user = await authenticate(req);
  const profile = await db.collection("users").doc(user.uid).get();
  if (!profile.exists || profile.get("role") !== "lawyer") throw new Error("FORBIDDEN");
  const offers = await db.collection("requestOffers").where("lawyerId", "==", user.uid)
    .where("status", "==", "offered").limit(20).get();
  res.status(200).json({offers: offers.docs.map((offer) => ({id: offer.id, ...offer.data()}))});
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
    if (legalRequest.get("status") !== "searching") throw new Error("CONFLICT");
    if (!(lawyer.get("licensedStates") as string[] | undefined)?.includes(legalRequest.get("state"))) throw new Error("FORBIDDEN");
    transaction.update(requestRef, {
      status: "assigned", assignedLawyerId: user.uid,
      assignedAt: FieldValue.serverTimestamp(),
      updatedAt: FieldValue.serverTimestamp(),
    });
    transaction.update(offerRef, {status: "accepted", acceptedAt: FieldValue.serverTimestamp()});
  });
  res.status(200).json({requestId, status: "assigned", lawyerId: user.uid});
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
    acceptingRequests: true, updatedAt: FieldValue.serverTimestamp(),
  }, {merge: true});
  res.status(200).json({ok: true, role: "lawyer"});
});
