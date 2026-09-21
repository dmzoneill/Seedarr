import React from "react";
import MediaArtwork, { type MediaArtworkProps } from "../MediaArtwork";

export interface MediaArtworkImageProps extends MediaArtworkProps {
  imageStyle?: React.CSSProperties;
  fallbackText?: string;
  onClick?: (e: React.MouseEvent<HTMLDivElement>) => void;
  objectFit?: "cover" | "contain" | "fill";
}

export const MediaArtworkImage: React.FC<MediaArtworkImageProps> = (props) => {
  return <MediaArtwork {...props} />;
};

export default MediaArtworkImage;
