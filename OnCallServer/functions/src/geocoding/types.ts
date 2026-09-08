export interface Coordinates {
  latitude: number;
  longitude: number;
}

export interface ClientLocationHint {
  city: string;
  state: string;
}

export interface Jurisdiction {
  city: string;
  state: string;
  county?: string;
  postalCode?: string;
  provider: string;
  verifiedByServer: boolean;
}

export interface ReverseGeocoder {
  reverse(coordinates: Coordinates, hint?: ClientLocationHint): Promise<Jurisdiction>;
}
