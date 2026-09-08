import {Coordinates, Jurisdiction, ReverseGeocoder} from "./types.js";

interface NominatimAddress {
  city?: string;
  town?: string;
  village?: string;
  municipality?: string;
  county?: string;
  state?: string;
  "ISO3166-2-lvl4"?: string;
  postcode?: string;
}

interface NominatimResponse { address?: NominatimAddress }

export class NominatimGeocoder implements ReverseGeocoder {
  constructor(private readonly baseUrl: string) {}

  async reverse(coordinates: Coordinates): Promise<Jurisdiction> {
    const url = new URL("reverse", `${this.baseUrl.replace(/\/$/, "")}/`);
    url.searchParams.set("format", "jsonv2");
    url.searchParams.set("addressdetails", "1");
    url.searchParams.set("lat", String(coordinates.latitude));
    url.searchParams.set("lon", String(coordinates.longitude));
    const response = await fetch(url, {headers: {"user-agent": "OnCallServer/1.0"}, signal: AbortSignal.timeout(5000)});
    if (!response.ok) throw new Error("GEOCODING_UNAVAILABLE");
    const result = await response.json() as NominatimResponse;
    const address = result.address;
    const city = address?.city ?? address?.town ?? address?.village ?? address?.municipality;
    const isoState = address?.["ISO3166-2-lvl4"];
    const state = isoState?.startsWith("US-") ? isoState.substring(3) : "";
    if (!city || !state) throw new Error("LOCATION_NOT_FOUND");
    return {
      city, state, county: address?.county, postalCode: address?.postcode,
      provider: "nominatim", verifiedByServer: true,
    };
  }
}
